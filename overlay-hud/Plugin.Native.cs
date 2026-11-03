using BepInEx.Configuration;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace OverlayHUD
{
    public sealed partial class Plugin
    {
        public enum RendererMode { Electron, Native }
        private bool nativeMode;
        private NativeHud nativeHud;
        private NativeTimestampWriter nativeTimestamps;
        private ConfigEntry<bool> nativeSaveTimestamps;
        private readonly NativeHudState nativeState = new NativeHudState();
        private ConfigEntry<float> nativeScale, nativeOffsetX, nativeOffsetY, nativeMonsterSize, nativeUpgradeSize;
        private ConfigEntry<int> nativeColumns, nativeStripColumns, nativeUpgradeRows;
        private ConfigEntry<string> nativeLanguage, nativeAlignment;
        private ConfigEntry<bool> nativeHorizontal, nativeEnabled, nativeGameUIEffects;
        private ConfigEntry<bool> nativeLevel, nativeTimer, nativeMapValue, nativeLostValue;
        private ConfigEntry<bool> nativeMonsters, nativeProximityWaves, nativeRespawnIndicator, nativeDestroyedItemAlerts;
        private ConfigEntry<bool> nativeHealth, nativeRespawn, nativeUpgrades, nativeStrength;
        private ConfigEntry<bool> nativeMonsterCounts, nativeDamageNumbers, nativeAnimations;
        private ConfigEntry<bool> nativeOnlyAliveMonsters, nativeFilterUndetectedMonsters, nativeWrapValueLabels, nativeTooltips;
        private readonly Dictionary<string, ConfigEntry<bool>> nativeUpgradeVisibility = new Dictionary<string, ConfigEntry<bool>>(StringComparer.Ordinal);
        private NativeHudOptions nativeOptions;
        private volatile bool nativeOptionsDirty = true;

        private void InitializeNativeHud()
        {
            nativeMode = Config.Bind("Overlay", "Renderer", RendererMode.Electron,
                "Electron or experimental Native Unity HUD. Restart the game after changing.").Value == RendererMode.Native;
            nativeEnabled = BindNativeToggle("Enabled", true, "Show the native HUD without stopping game-state tracking.");
            nativeSaveTimestamps = Config.Bind("NativeHUD", "SaveTimestamps", true,
                "Save level start times, map names and seen monster names/counts in BepInEx/plugins/OverlayHUD/timestamps. Disabling finalizes the current line; enabling starts at the next level.");
            nativeLanguage = Config.Bind("NativeHUD", "Language", "ru",
                new ConfigDescription("Native HUD language: ru (Russian) or en (English). Applies immediately.", new AcceptableValueList<string>("ru", "en")));
            nativeAlignment = Config.Bind("NativeHUD", "Alignment", "Center",
                new ConfigDescription("Horizontal screen alignment of the HUD: Left, Center or Right. OffsetX shifts it from this anchor. Applies immediately.", new AcceptableValueList<string>("Left", "Center", "Right")));
            nativeScale = Config.Bind("NativeHUD", "Scale", 1f,
                new ConfigDescription("HUD scale relative to 1080p. Applies immediately.", new AcceptableValueRange<float>(0.5f, 2f)));
            nativeOffsetX = Config.Bind("NativeHUD", "OffsetX", 0f,
                new ConfigDescription("Horizontal offset from the selected Alignment in scaled HUD pixels; positive moves right. Applies immediately.", new AcceptableValueRange<float>(-1920f, 1920f)));
            nativeOffsetY = Config.Bind("NativeHUD", "OffsetY", 20f,
                new ConfigDescription("Distance from screen top in scaled HUD pixels. Applies immediately.", new AcceptableValueRange<float>(0f, 1080f)));
            nativeHorizontal = BindNativeToggle("HorizontalLayout", true,
                "Use the compact Electron-style topbar and horizontal monster strip. True uses HorizontalColumns; false uses the grid layout and Columns. Neither setting changes monster visibility.");
            nativeStripColumns = Config.Bind("NativeHUD", "HorizontalColumns", 11,
                new ConfigDescription("Maximum monster tiles per row when HorizontalLayout is true; extra tiles wrap onto another row. Ignored when false. Applies immediately.", new AcceptableValueRange<int>(1, 24)));
            nativeColumns = Config.Bind("NativeHUD", "Columns", 4,
                new ConfigDescription("Maximum monster tiles per row when HorizontalLayout is false (grid layout); extra tiles wrap onto another row. Ignored when true; use HorizontalColumns instead. Applies immediately.", new AcceptableValueRange<int>(1, 24)));
            nativeMonsterSize = Config.Bind("NativeHUD", "MonsterSize", 70f,
                new ConfigDescription("Monster tile size in HUD pixels before Scale is applied. Applies immediately.", new AcceptableValueRange<float>(32f, 128f)));
            nativeUpgradeSize = Config.Bind("NativeHUD", "UpgradeSize", 38f,
                new ConfigDescription("Player upgrade icon size in HUD pixels before Scale is applied. Applies immediately.", new AcceptableValueRange<float>(20f, 64f)));
            nativeUpgradeRows = Config.Bind("NativeHUD", "UpgradeRows", 2,
                new ConfigDescription("Number of rows used to arrange visible player upgrades (1 or 2). Applies immediately.", new AcceptableValueRange<int>(1, 2)));
            nativeLevel = BindNativeToggle("ShowLevel", true, "Show the current level number.");
            nativeTimer = BindNativeToggle("ShowTimer", true, "Show the elapsed level timer.");
            nativeMapValue = BindNativeToggle("ShowMapValue", true, "Show the value of valuables remaining on the map.");
            nativeLostValue = BindNativeToggle("ShowLostValue", true, "Show the lost value of valuables.");
            nativeMonsters = BindNativeToggle("ShowMonsters", true, "Show the monster tiles; does not hide the level, timer, values or upgrades.");
            nativeProximityWaves = BindNativeToggle("ShowProximityWaves", true, "Show proximity waves around nearby monster tiles.");
            nativeRespawnIndicator = BindNativeToggle("ShowRespawnIndicator", true, "Show the circular monster respawn indicator, independently of ShowRespawnTimer.");
            nativeDestroyedItemAlerts = BindNativeToggle("ShowDestroyedItemAlerts", true, "Show notifications about destroyed valuables and their last holder.");
            nativeHealth = BindNativeToggle("ShowHealth", true, "Show monster health bars. Numeric HP is available in hover tooltips when ShowTooltips is enabled.");
            nativeRespawn = BindNativeToggle("ShowRespawnTimer", true, "Show seen monsters' numeric respawn timers, independently of ShowRespawnIndicator.");
            nativeStrength = BindNativeToggle("ShowStrength", true, "Show required strength on seen monsters, including during respawn. Green means sufficient strength, red insufficient; amber X means impossible and ? unknown.");
            nativeUpgrades = BindNativeToggle("ShowUpgrades", true, "Show player upgrade icons; individual upgrades can also be hidden in NativeHUD.Upgrades.");
            nativeMonsterCounts = BindNativeToggle("ShowMonsterCounts", true, "Show counts on grouped monster tiles.");
            nativeDamageNumbers = BindNativeToggle("ShowDamageNumbers", true, "Show floating monster damage numbers.");
            nativeAnimations = BindNativeToggle("Animations", true, "Animate HUD transitions and effects. False also disables waves, damage flashes and floating damage numbers; numeric timers still update.");
            nativeOnlyAliveMonsters = BindNativeToggle("OnlyAliveMonsters", false, "Hide dead detected monsters until they respawn; does not reveal undetected monsters.");
            nativeFilterUndetectedMonsters = BindNativeToggle("FilterUndetectedMonsters", false, "When OnlyAliveMonsters is true, also remove dead undetected placeholders. False keeps all undetected placeholders. Ignored when OnlyAliveMonsters is false.");
            nativeWrapValueLabels = BindNativeToggle("WrapValueLabels", true, "Place map/loss labels above their amounts; false keeps labels and amounts on one line.");
            nativeTooltips = BindNativeToggle("ShowTooltips", false, "Show monster and upgrade tooltips on hover while the game cursor is unlocked. Does not unlock or capture the cursor. Tooltips are disabled while UseGameUIEffects is active.");
            nativeGameUIEffects = BindNativeToggle("UseGameUIEffects", false,
                "Experimental: render the native HUD through the game's UI camera and compositor to inherit real game HUD effects. The game's lower-resolution render texture reduces HUD sharpness; hover tooltips are disabled while active. Falls back to the normal HUD while the game UI is unavailable.");

            BindNativeUpgrade("strength", "Strength");
            BindNativeUpgrade("tumbleLaunch", "Tumble Launch");
            BindNativeUpgrade("range", "Range");
            BindNativeUpgrade("sprintSpeed", "Sprint Speed");
            BindNativeUpgrade("tumbleWings", "Tumble Wings");
            BindNativeUpgrade("crouchRest", "Crouch Rest");
            BindNativeUpgrade("extraJump", "Extra Jump");
            BindNativeUpgrade("tumbleClimb", "Tumble Climb");
            BindNativeUpgrade("health", "Health");
            BindNativeUpgrade("stamina", "Stamina");
            BindNativeUpgrade("mapPlayerCount", "Map Player Count");
            BindNativeUpgrade("deathHeadBattery", "Death Head Battery");

            Config.SettingChanged -= OnNativeSettingChanged;
            Config.SettingChanged += OnNativeSettingChanged;
            nativeOptionsDirty = true;
            if (!nativeMode) return;
            try
            {
                NativeHudOptions options = GetNativeOptions();
                nativeHud = new NativeHud(nativeState, options.Columns, options.HorizontalLayout,
                    message => Logger.LogInfo(message));
                nativeHud.Configure(options);
                nativeTimestamps = new NativeTimestampWriter(nativeState,
                    System.IO.Path.Combine(BepInEx.Paths.PluginPath, "OverlayHUD", "timestamps"),
                    error => Logger.LogWarning("Native timestamp saving disabled for this session: " + error.Message));
                Logger.LogInfo("Native HUD initialized with bundled Teko and Roboto font atlases. Desktop app and HTTP bridge disabled.");
            }
            catch (Exception error)
            {
                nativeHud?.Dispose();
                nativeHud = null;
                // Keep the desktop path usable if a resource or Unity component is missing.
                nativeMode = false;
                Logger.LogError("Native HUD initialization failed; using Electron for this session: " + error);
            }
        }

        private ConfigEntry<bool> BindNativeToggle(string key, bool defaultValue, string description)
        {
            return Config.Bind("NativeHUD", key, defaultValue, description + " Applies immediately.");
        }

        private void BindNativeUpgrade(string key, string label)
        {
            nativeUpgradeVisibility[key] = Config.Bind("NativeHUD.Upgrades", key, true,
                "Show the " + label + " upgrade when owned and ShowUpgrades is enabled. Applies immediately.");
        }

        private void OnNativeSettingChanged(object sender, SettingChangedEventArgs args)
        {
            string section = args.ChangedSetting.Definition.Section;
            if (section == "NativeHUD" || section == "NativeHUD.Upgrades")
                nativeOptionsDirty = true;
        }

        private NativeHudOptions GetNativeOptions()
        {
            if (!nativeOptionsDirty && nativeOptions != null) return nativeOptions;
            // Clear first so a setting changed during the rebuild is picked up next tick.
            nativeOptionsDirty = false;
            var options = new NativeHudOptions
            {
                Scale = nativeScale.Value,
                OffsetX = nativeOffsetX.Value,
                OffsetY = nativeOffsetY.Value,
                MonsterSize = nativeMonsterSize.Value,
                UpgradeSize = nativeUpgradeSize.Value,
                Columns = nativeColumns.Value,
                HorizontalColumns = nativeStripColumns.Value,
                UpgradeRows = nativeUpgradeRows.Value,
                HorizontalLayout = nativeHorizontal.Value,
                Enabled = nativeEnabled.Value,
                Language = nativeLanguage.Value,
                Alignment = nativeAlignment.Value,
                ShowLevel = nativeLevel.Value,
                ShowTimer = nativeTimer.Value,
                ShowMapValue = nativeMapValue.Value,
                ShowLostValue = nativeLostValue.Value,
                ShowMonsters = nativeMonsters.Value,
                ShowProximityWaves = nativeProximityWaves.Value,
                ShowRespawnIndicator = nativeRespawnIndicator.Value,
                ShowDestroyedItemAlerts = nativeDestroyedItemAlerts.Value,
                ShowHealth = nativeHealth.Value,
                ShowRespawnTimer = nativeRespawn.Value,
                ShowStrength = nativeStrength.Value,
                ShowUpgrades = nativeUpgrades.Value,
                ShowMonsterCounts = nativeMonsterCounts.Value,
                ShowDamageNumbers = nativeDamageNumbers.Value,
                Animations = nativeAnimations.Value,
                OnlyAliveMonsters = nativeOnlyAliveMonsters.Value,
                FilterUndetectedMonsters = nativeFilterUndetectedMonsters.Value,
                WrapValueLabels = nativeWrapValueLabels.Value,
                ShowTooltips = nativeTooltips.Value,
                UseGameUIEffects = nativeGameUIEffects.Value
            };
            foreach (var upgrade in nativeUpgradeVisibility)
                if (!upgrade.Value.Value) options.HiddenUpgrades.Add(upgrade.Key);
            nativeOptions = options;
            return options;
        }

        private void DisposeNativeConfig()
        {
            Config.SettingChanged -= OnNativeSettingChanged;
            nativeTimestamps?.Dispose();
        }

        private void TickNativeHud()
        {
            // PostVisibility changes Visible directly, bypassing DispatchNative.
            nativeTimestamps?.Tick(Time.realtimeSinceStartup, nativeSaveTimestamps.Value);
            if (nativeHud == null) return;
            nativeHud.Configure(GetNativeOptions());
            nativeHud.Tick(Time.realtimeSinceStartup);
        }

        private bool DispatchNative(string route, string json)
        {
            if (!nativeMode) return false;
            double now = Time.realtimeSinceStartup;
            nativeTimestamps?.Tick(now, nativeSaveTimestamps.Value);
            if (route == "level")
                nativeTimestamps?.BeforeLevel(json, now, nativeSaveTimestamps.Value, nativeLanguage.Value);
            nativeState.Apply(route, json, now);
            return true;
        }
    }
}
