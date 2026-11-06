using System;
using System.Collections.Generic;

namespace OverlayHUD
{
    // Names only: this cache has no roster, encounter state, or localized identity keys.
    internal sealed class HudNameLocalization
    {
        // HUD shared keys verified in REPO_Data/StreamingAssets/aa/StandaloneWindows64/
        // localization-assets-shared_assets_all.bundle. Values come from HUD_en-US/HUD_ru-RU
        // at runtime, not hand-written translations. Keep canonical HUD identities stable.
        private static readonly Dictionary<string, string> MonsterKeys = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "Animal", "ENEMY.ANIMAL" },
            { "Banger", "ENEMY.BANG" },
            { "Clown", "ENEMY.BEAMER" },
            { "Birthday Boy", "ENEMY.BIRTHDAY_BOY" },
            { "Cleanup Crew", "ENEMY.BOMB_THROWER" },
            { "Bowtie", "ENEMY.BOWTIE" },
            { "Peeper", "ENEMY.CEILING_EYE" },
            { "Apex Predator", "ENEMY.DUCK" },
            { "Elsa", "ENEMY.ELSA" },
            { "Mentalist", "ENEMY.FLOATER" },
            { "Gnome", "ENEMY.GNOME" },
            { "Gnomes", "ENEMY.GNOME" },
            { "Headman", "ENEMY.HEAD" },
            { "Headgrab", "ENEMY.HEAD_GRABBER" },
            { "Heart Hugger", "ENEMY.HEART_HUGGER" },
            { "Hidden", "ENEMY.HIDDEN" },
            { "Huntsman", "ENEMY.HUNTER" },
            { "Oogly", "ENEMY.OOGLY" },
            { "Robe", "ENEMY.ROBE" },
            { "Reaper", "ENEMY.RUNNER" },
            { "Loom", "ENEMY.SHADOW" },
            { "Spewer", "ENEMY.SLOW_MOUTH" },
            { "Trudge", "ENEMY.SLOW_WALKER" },
            { "Gambit", "ENEMY.SPINNY" },
            { "Shadow Child", "ENEMY.THIN_MAN" },
            { "Tick", "ENEMY.TICK" },
            { "Bella", "ENEMY.TRICYCLE" },
            { "Chef", "ENEMY.TUMBLER" },
            { "Upscream", "ENEMY.UPSCREAM" },
            { "Rugrat", "ENEMY.VALUABLE_THROWER" },
            { "Snatcher", "ENEMY.SNATCHER" },
            { "Pony", "ENEMY.SNATCHER" }
        };

        // STATS.UPGRADE.* are the official short stat labels; ITEM.UPGRADE_* are
        // full shop item names (with "Upgrade" / "(улучшение)"), not tooltip labels.
        private static readonly Dictionary<string, string> UpgradeKeys = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "strength", "STATS.UPGRADE.STRENGTH" },
            { "tumbleLaunch", "STATS.UPGRADE.LAUNCH" },
            { "range", "STATS.UPGRADE.RANGE" },
            { "sprintSpeed", "STATS.UPGRADE.SPEED" },
            { "tumbleWings", "STATS.UPGRADE.TUMBLE_WINGS" },
            { "crouchRest", "STATS.UPGRADE.CROUCH_REST" },
            { "extraJump", "STATS.UPGRADE.EXTRA_JUMP" },
            { "tumbleClimb", "STATS.UPGRADE.TUMBLE_CLIMB" },
            { "health", "STATS.UPGRADE.HEALTH" },
            { "stamina", "STATS.UPGRADE.STAMINA" },
            { "mapPlayerCount", "STATS.UPGRADE.MAP_PLAYER_COUNT" },
            { "deathHeadBattery", "STATS.UPGRADE.DEATH_HEAD_BATTERY" }
        };

        // Verified HUD_en-US values, also usable before Addressables initialization.
        private static readonly Dictionary<string, string> UpgradeFallbacks = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "strength", "Strength" }, { "tumbleLaunch", "Tumble Launch" },
            { "range", "Range" }, { "sprintSpeed", "Sprint Speed" },
            { "tumbleWings", "Tumble Wings" }, { "crouchRest", "Crouch Rest" },
            { "extraJump", "Extra Jump" }, { "tumbleClimb", "Tumble Climb" },
            { "health", "Health" }, { "stamina", "Stamina" },
            { "mapPlayerCount", "Map Player Count" }, { "deathHeadBattery", "Death Head Battery" }
        };

        private readonly Dictionary<string, Dictionary<string, string>> tables =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

        internal void Clear() => tables.Clear();

        internal bool SetTable(string language, IEnumerable<KeyValuePair<string, string>> entries)
        {
            if ((language != "en" && language != "ru") || entries == null) return false;
            var names = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var entry in entries)
            {
                if (entry.Key == null || (!entry.Key.StartsWith("ENEMY.", StringComparison.Ordinal)
                    && !entry.Key.StartsWith("STATS.UPGRADE.", StringComparison.Ordinal))
                    || string.IsNullOrWhiteSpace(entry.Value) || entry.Value == entry.Key) continue;
                names[entry.Key] = entry.Value;
            }
            if (names.Count == 0) return false;
            tables[language] = names;
            return true;
        }

        internal string MonsterName(string canonicalName, string language, bool seen, bool unknown)
        {
            // Defense in depth: even a caller holding a hidden roster name gets no label.
            if (!seen || unknown) return null;
            return canonicalName != null && MonsterKeys.TryGetValue(canonicalName, out string key)
                ? Name(key, language, canonicalName) : canonicalName ?? "";
        }

        internal string UpgradeName(string upgrade, string language)
        {
            return upgrade != null && UpgradeKeys.TryGetValue(upgrade, out string key)
                ? Name(key, language, UpgradeFallbacks[upgrade]) : upgrade ?? "";
        }

        private string Name(string key, string language, string fallback)
        {
            // Same ru/en default as NativeHudOptions; never use the game's selected locale.
            if (tables.TryGetValue(language == "en" ? "en" : "ru", out var localized)
                && localized.TryGetValue(key, out string value)) return value;
            if (tables.TryGetValue("en", out var english) && english.TryGetValue(key, out value)) return value;
            return fallback;
        }

        internal Dictionary<string, Dictionary<string, string>> EnemyNames() => ExportNames("ENEMY.");

        internal Dictionary<string, Dictionary<string, string>> UpgradeNames() => ExportNames("STATS.UPGRADE.");

        private Dictionary<string, Dictionary<string, string>> ExportNames(string prefix)
        {
            var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
            foreach (var table in tables)
                foreach (var entry in table.Value)
                {
                    if (!entry.Key.StartsWith(prefix, StringComparison.Ordinal)) continue;
                    if (!result.TryGetValue(entry.Key, out var translations))
                        result[entry.Key] = translations = new Dictionary<string, string>(StringComparer.Ordinal);
                    translations[table.Key] = entry.Value;
                }
            return result;
        }
    }
}
