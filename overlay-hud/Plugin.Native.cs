using BepInEx.Configuration;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace OverlayHUD
{
    public sealed partial class Plugin
    {
#if !NATIVE_ONLY
        public enum RendererMode
        {
            Electron,
            Native
        }
#endif
        private const string NativeGeneralSection = "03 Native - General";
        private const string NativeLayoutSection = "04 Native - Layout";
        private const string NativeInformationSection = "05 Native - Information";
        private const string NativeMonstersSection = "06 Native - Monsters";
        private const string NativeEffectsSection = "07 Native - Effects";
        private const string NativeUpgradesSection = "08 Native - Upgrades";
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
#if NATIVE_ONLY
            nativeMode = true;
#else
            nativeMode = BindNativeSetting("01 General", "Renderer", RendererMode.Electron,
                "Electron desktop HUD (default) or Native Unity HUD. Restart the game after changing.", 100).Value == RendererMode.Native;
#endif
            nativeEnabled = BindNativeToggle(NativeGeneralSection, "Enabled", true, "Show the native HUD without stopping game-state tracking.", 100);
            nativeLanguage = BindNativeSetting(NativeGeneralSection, "Language", "ru",
                "Native HUD language: ru (Russian) or en (English). Applies immediately.", 90, new AcceptableValueList<string>("ru", "en"));
            nativeSaveTimestamps = BindNativeSetting(NativeGeneralSection, "SaveTimestamps", true,
                "Save level start times, map names and seen monster names/counts in BepInEx/plugins/OverlayHUD/timestamps. Disabling finalizes the current line; enabling starts at the next level.", 80);

            nativeAlignment = BindNativeSetting(NativeLayoutSection, "Alignment", "Center",
                "Horizontal screen alignment of the HUD: Left, Center or Right. OffsetX shifts it from this anchor. Applies immediately.", 120, new AcceptableValueList<string>("Left", "Center", "Right"));
            nativeOffsetX = BindNativeSetting(NativeLayoutSection, "OffsetX", 0f,
                "Horizontal offset from the selected Alignment in scaled HUD pixels; positive moves right. Applies immediately.", 110, new AcceptableValueRange<float>(-1920f, 1920f));
            nativeOffsetY = BindNativeSetting(NativeLayoutSection, "OffsetY", 20f,
                "Distance from screen top in scaled HUD pixels. Applies immediately.", 100, new AcceptableValueRange<float>(0f, 1080f));
            nativeScale = BindNativeSetting(NativeLayoutSection, "Scale", 1f,
                "HUD scale relative to 1080p. Applies immediately.", 90, new AcceptableValueRange<float>(0.5f, 2f));
            nativeHorizontal = BindNativeToggle(NativeLayoutSection, "HorizontalLayout", true,
                "Use the compact topbar and horizontal monster strip. True uses HorizontalColumns; false uses the grid layout and Columns. Neither setting changes monster visibility.", 80);
            nativeStripColumns = BindNativeSetting(NativeLayoutSection, "HorizontalColumns", 11,
                "Maximum monster tiles per row when HorizontalLayout is true; extra tiles wrap onto another row. Ignored when false. Applies immediately.", 70, new AcceptableValueRange<int>(1, 24));
            nativeColumns = BindNativeSetting(NativeLayoutSection, "Columns", 4,
                "Maximum monster tiles per row when HorizontalLayout is false (grid layout); extra tiles wrap onto another row. Ignored when true; use HorizontalColumns instead. Applies immediately.", 60, new AcceptableValueRange<int>(1, 24));
            nativeMonsterSize = BindNativeSetting(NativeLayoutSection, "MonsterSize", 70f,
                "Monster tile size in HUD pixels before Scale is applied. Applies immediately.", 50, new AcceptableValueRange<float>(32f, 128f));
            nativeUpgradeSize = BindNativeSetting(NativeLayoutSection, "UpgradeSize", 38f,
                "Player upgrade icon size in HUD pixels before Scale is applied. Applies immediately.", 40, new AcceptableValueRange<float>(20f, 64f));
            nativeUpgradeRows = BindNativeSetting(NativeLayoutSection, "UpgradeRows", 2,
                "Number of rows used to arrange visible player upgrades (1 or 2). Applies immediately.", 30, new AcceptableValueRange<int>(1, 2));
            nativeWrapValueLabels = BindNativeToggle(NativeLayoutSection, "WrapValueLabels", true, "Place map/loss labels above their amounts; false keeps labels and amounts on one line.", 20);

            nativeLevel = BindNativeToggle(NativeInformationSection, "ShowLevel", true, "Show the current level number.", 100);
            nativeTimer = BindNativeToggle(NativeInformationSection, "ShowTimer", true, "Show the elapsed level timer.", 90);
            nativeMapValue = BindNativeToggle(NativeInformationSection, "ShowMapValue", true, "Show the value of valuables remaining on the map.", 80);
            nativeLostValue = BindNativeToggle(NativeInformationSection, "ShowLostValue", true, "Show the lost value of valuables.", 70);
            nativeUpgrades = BindNativeToggle(NativeInformationSection, "ShowUpgrades", true, "Show player upgrade icons; individual upgrades can also be hidden in 08 Native - Upgrades.", 60);

            nativeMonsters = BindNativeToggle(NativeMonstersSection, "ShowMonsters", true, "Show the monster tiles; does not hide the level, timer, values or upgrades.", 100);
            nativeHealth = BindNativeToggle(NativeMonstersSection, "ShowHealth", true, "Show monster health bars. Numeric HP is available in hover tooltips when ShowTooltips is enabled.", 90);
            nativeRespawn = BindNativeToggle(NativeMonstersSection, "ShowRespawnTimer", true, "Show seen monsters' numeric respawn timers, independently of ShowRespawnIndicator.", 80);
            nativeRespawnIndicator = BindNativeToggle(NativeMonstersSection, "ShowRespawnIndicator", true, "Show the circular monster respawn indicator, independently of ShowRespawnTimer.", 70);
            nativeStrength = BindNativeToggle(NativeMonstersSection, "ShowStrength", true, "Show required strength on seen monsters, including during respawn. Green means sufficient strength, red insufficient; amber X means impossible and ? unknown.", 60);
            nativeMonsterCounts = BindNativeToggle(NativeMonstersSection, "ShowMonsterCounts", true, "Show counts on grouped monster tiles.", 50);
            nativeOnlyAliveMonsters = BindNativeToggle(NativeMonstersSection, "OnlyAliveMonsters", false, "Hide dead detected monsters until they respawn; does not reveal undetected monsters.", 40);
            nativeFilterUndetectedMonsters = BindNativeToggle(NativeMonstersSection, "FilterUndetectedMonsters", false, "When OnlyAliveMonsters is true, also remove dead undetected placeholders. False keeps all undetected placeholders. Ignored when OnlyAliveMonsters is false.", 30);

            nativeAnimations = BindNativeToggle(NativeEffectsSection, "Animations", true, "Animate HUD transitions and effects. False also disables waves, damage flashes and floating damage numbers; numeric timers still update.", 100);
            nativeProximityWaves = BindNativeToggle(NativeEffectsSection, "ShowProximityWaves", true, "Show proximity waves around nearby monster tiles.", 90);
            nativeDamageNumbers = BindNativeToggle(NativeEffectsSection, "ShowDamageNumbers", true, "Show floating monster damage numbers.", 80);
            nativeDestroyedItemAlerts = BindNativeToggle(NativeEffectsSection, "ShowDestroyedItemAlerts", false, "Show notifications about destroyed valuables and their last holder.", 70);
            nativeGameUIEffects = BindNativeToggle(NativeEffectsSection, "UseGameUIEffects", true,
                "Experimental: render the native HUD through the game's UI camera and compositor to inherit real game HUD effects. The game's lower-resolution render texture reduces HUD sharpness; hover tooltips are disabled while active. Falls back to the normal HUD while the game UI is unavailable.", 60);
            nativeTooltips = BindNativeToggle(NativeEffectsSection, "ShowTooltips", false, "Show monster and upgrade tooltips on hover while the game cursor is unlocked. Does not unlock or capture the cursor. Tooltips are disabled while UseGameUIEffects is active.", 50);

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
                Logger.LogInfo("Native HUD initialized with bundled Teko and Roboto font atlases.");
            }
            catch (Exception error)
            {
                nativeHud?.Dispose();
                nativeHud = null;
#if NATIVE_ONLY
                Logger.LogError("Native HUD initialization failed: " + error);
#else
                // Keep the desktop path usable if a resource or Unity component is missing.
                nativeMode = false;
                Logger.LogError("Native HUD initialization failed; using Electron for this session: " + error);
#endif
            }
        }

        // ConfigurationManager reads public tag fields by name; no dependency on that optional plugin.
        private sealed class NativeConfigurationManagerAttributes
        {
            public int Order;
        }

        private ConfigEntry<T> BindNativeSetting<T>(string section, string key, T defaultValue,
            string description, int order, AcceptableValueBase acceptableValues = null)
        {
            return Config.Bind(section, key, defaultValue, new ConfigDescription(description, acceptableValues,
                new NativeConfigurationManagerAttributes { Order = order }));
        }

        private ConfigEntry<bool> BindNativeToggle(string section, string key, bool defaultValue, string description, int order)
        {
            return BindNativeSetting(section, key, defaultValue, description + " Applies immediately.", order);
        }

        private void BindNativeUpgrade(string key, string label)
        {
            nativeUpgradeVisibility[key] = BindNativeToggle(NativeUpgradesSection, key, true,
                "Show the " + label + " upgrade when owned and ShowUpgrades is enabled.", 100 - nativeUpgradeVisibility.Count);
        }

        private void OnNativeSettingChanged(object sender, SettingChangedEventArgs args)
        {
            string section = args.ChangedSetting.Definition.Section;
            if (section == NativeGeneralSection || section == NativeLayoutSection
                || section == NativeInformationSection || section == NativeMonstersSection
                || section == NativeEffectsSection || section == NativeUpgradesSection)
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
