using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace OverlayHUD
{
    // Receives the same events as the desktop backend, on Unity's main thread.
    // Roster/status events must never reveal an unseen enemy.
    internal sealed class NativeHudState
    {
        internal sealed class Monster
        {
            internal int Id;
            internal int Count = 1;
            internal bool Unknown;
            internal int? Tier;
            internal string Name;
            internal bool Seen, Alive = true;
            internal bool PlayerClose, PlayerVeryClose;
            internal float? Health, MaxHealth;
            internal double RespawnAt;
            internal double RespawnDuration;
            internal double CooldownReducedAt = -10;
            internal double? RequiredStrength;
            internal bool StrengthImpossible;
        }

        internal sealed class DestroyedItemAlert
        {
            internal string Player, Item;
            internal double At;
            internal long Sequence;
        }

        private sealed class DisplayGroup
        {
            internal string Name;
            internal int? Tier;
            internal readonly List<int> Members = new List<int>();
        }

        private readonly List<DisplayGroup> displayGroups = new List<DisplayGroup>();
        private readonly Dictionary<int, DisplayGroup> groupById = new Dictionary<int, DisplayGroup>();
        internal readonly Dictionary<int, Monster> Monsters = new Dictionary<int, Monster>();
        internal readonly Dictionary<string, int> Upgrades = new Dictionary<string, int>();
        internal int MapValue, LostValue;
        internal int Level;
        internal bool Visible, Hidden;
        internal double StartedAt;
        internal DestroyedItemAlert LastDestroyedItem;
        private long destroyedItemSequence;

        internal void Apply(string route, string json, double now)
        {
            JObject data = JObject.Parse(json);
            switch (route)
            {
                case "level":
                    double? level = FiniteNumber(data["level"]);
                    if (!level.HasValue || level < 1 || level > int.MaxValue) break;
                    Level = (int)Math.Floor(level.Value);
                    Monsters.Clear();
                    displayGroups.Clear();
                    groupById.Clear();
                    Upgrades.Clear();
                    LastDestroyedItem = null;
                    MapValue = LostValue = 0;
                    StartedAt = now;
                    Visible = true;
                    break;
                case "visibility": Visible = (bool)data["visible"]; break;
                case "tab-hidden": Hidden = (bool?)data["hidden"] ?? false; break;
                case "monster-seen": Merge(data, now, true); break;
                case "item-destroyed":
                    string player = SanitizeAlertName(data["player"], 60);
                    string item = SanitizeAlertName(data["item"], 80);
                    if (player.Length == 0 || item.Length == 0 || double.IsNaN(now) || double.IsInfinity(now)) break;
                    LastDestroyedItem = new DestroyedItemAlert
                    {
                        Player = player, Item = item, At = now, Sequence = ++destroyedItemSequence
                    };
                    break;
                case "upgrades":
                    foreach (var entry in ((JObject)(data["upgrades"] ?? data)).Properties())
                        Upgrades[entry.Name] = Math.Max(0, (int)entry.Value);
                    break;
                case "map-value":
                    MapValue = Math.Max(0, (int)data["value"]);
                    LostValue = Math.Max(0, (int?)data["lost"] ?? 0);
                    break;
                case "roster":
                case "monster-status":
                    var entries = data[route == "roster" ? "monsters" : "statuses"] as JArray;
                    if (entries != null)
                        foreach (JObject entry in entries) Merge(entry, now, false);
                    break;
            }
        }

        private static string SanitizeAlertName(JToken token, int limit)
        {
            if (token?.Type != JTokenType.String) return "";
            var result = new System.Text.StringBuilder(limit);
            bool space = false;
            foreach (char c in (string)token)
            {
                if (char.IsWhiteSpace(c)) { space = result.Length > 0; continue; }
                if (char.IsControl(c) || char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.Format) continue;
                if (space && result.Length < limit) result.Append(' ');
                space = false;
                if (result.Length >= limit) break;
                result.Append(c);
            }
            // Do not leave half of a supplementary character at the cap.
            if (result.Length > 0 && char.IsHighSurrogate(result[result.Length - 1])) result.Length--;
            return result.ToString().TrimEnd();
        }

        private void Merge(JObject data, double now, bool seen)
        {
            int id = (int)data["id"];
            if (!Monsters.TryGetValue(id, out Monster monster))
                Monsters.Add(id, monster = new Monster { Id = id });
            if (data["name"] != null) monster.Name = (string)data["name"];
            monster.Seen |= seen;
            // Plugin metadata omits this flag for known enemies; partial events must retain it.
            monster.Unknown |= (bool?)data["unknown"] == true;
            if (data["tier"] != null)
                monster.Tier = data["tier"].Type == JTokenType.Null ? (int?)null : (int)data["tier"];
            if (data.Property("strength") != null)
            {
                JToken strength = data["strength"];
                monster.StrengthImpossible = strength.Type == JTokenType.String && (string)strength == "impossible";
                monster.RequiredStrength = monster.StrengthImpossible ? null
                    : NormalizeDynamicStrength(strength, data["strengthForceMax"]);
            }
            UpdateDisplayGroup(monster);
            bool wasAlive = monster.Alive;
            if (data["alive"] != null) monster.Alive = (bool)data["alive"];
            if (data["playerClose"]?.Type == JTokenType.Boolean)
                monster.PlayerClose = (bool)data["playerClose"];
            if (data["playerVeryClose"]?.Type == JTokenType.Boolean)
                monster.PlayerVeryClose = (bool)data["playerVeryClose"];
            if (!monster.Alive) monster.PlayerClose = monster.PlayerVeryClose = false;
            monster.Health = MergeHealth(monster.Health, data["health"]);
            monster.MaxHealth = MergeHealth(monster.MaxHealth, data["maxHealth"]);
            // Metadata-only roster/seen events must not erase a cached countdown.
            if (data["alive"] != null || data["respawnRemaining"] != null)
            {
                double remaining = FiniteNumber(data["respawnRemaining"]) ?? 0;
                if (monster.Alive || remaining <= 0)
                {
                    monster.RespawnAt = monster.RespawnDuration = 0;
                    monster.CooldownReducedAt = -10;
                }
                else
                {
                    double reduction = Math.Max(0, monster.RespawnAt - now) - remaining;
                    bool firstCountdown = wasAlive || monster.RespawnAt <= 0;
                    if (firstCountdown) monster.CooldownReducedAt = -10;
                    else if (reduction > 1.5) monster.CooldownReducedAt = now;
                    // Keep the web's maximum duration for this death, even after a reduction.
                    monster.RespawnDuration = wasAlive ? remaining : Math.Max(monster.RespawnDuration, remaining);
                    if (firstCountdown || Math.Abs(reduction) >= 1.5)
                        // Match the server's jitter tolerance, but accept real timer corrections.
                        monster.RespawnAt = now + remaining;
                }
            }
        }

        private static string GroupName(string name)
        {
            // Only aliases with a non-null getMonsterCount in server.js are groupable.
            // Spawn tier is metadata, not the species level used by that function.
            string key = System.Text.RegularExpressions.Regex.Replace((name ?? "").ToLowerInvariant(), "[^a-z0-9]", "");
            switch (key)
            {
                case "gnome": case "gnomes": return "Gnomes";
                case "banger": return "Banger";
                case "bangers": return "Bangers";
                case "animals": return "Animals";
                case "upscreams": return "Upscreams";
                case "bowties": return "Bowties";
                case "mentalists": return "Mentalists";
                case "peepers": return "Peepers";
                case "chefs": return "Chefs";
                case "spewers": return "Spewers";
                default: return null;
            }
        }

        private static int GroupSize(string name)
        {
            switch (name)
            {
                case "Gnomes": case "Spewers": return 4;
                case "Bangers": return 6;
                default: return 3;
            }
        }

        private void UpdateDisplayGroup(Monster monster)
        {
            string name = monster.Unknown ? null : GroupName(monster.Name);
            if (groupById.TryGetValue(monster.Id, out DisplayGroup previous))
            {
                if (previous.Name == name && previous.Tier == monster.Tier) return;
                previous.Members.Remove(monster.Id);
                groupById.Remove(monster.Id);
                if (previous.Members.Count == 0) displayGroups.Remove(previous);
            }
            if (name == null) return;
            // Persist membership rather than rechunking a dictionary on each render:
            // late metadata and reordered/repeated rosters must not shuffle full groups.
            DisplayGroup group = displayGroups.Find(candidate => candidate.Name == name
                && candidate.Tier == monster.Tier && candidate.Members.Count < GroupSize(name));
            if (group == null)
            {
                group = new DisplayGroup { Name = name, Tier = monster.Tier };
                displayGroups.Add(group);
            }
            group.Members.Add(monster.Id);
            groupById.Add(monster.Id, group);
        }

        internal List<Monster> GetDisplayMonsters(double now)
        {
            var result = new List<Monster>();
            foreach (DisplayGroup group in displayGroups)
            {
                var display = new Monster
                {
                    Id = group.Members[0], Name = group.Name, Tier = group.Tier,
                    Count = group.Members.Count, Alive = false
                };
                foreach (int id in group.Members)
                {
                    Monster member = Monsters[id];
                    display.Seen |= member.Seen;
                    display.Alive |= member.Alive;
                    if (member.Seen && member.Alive)
                    {
                        display.PlayerClose |= member.PlayerClose;
                        display.PlayerVeryClose |= member.PlayerVeryClose;
                    }
                    display.StrengthImpossible |= member.StrengthImpossible;
                    if (member.RequiredStrength.HasValue)
                        display.RequiredStrength = Math.Max(display.RequiredStrength ?? 0, member.RequiredStrength.Value);
                    if (member.Health.HasValue) display.Health = (display.Health ?? 0) + member.Health.Value;
                    if (member.MaxHealth.HasValue) display.MaxHealth = (display.MaxHealth ?? 0) + member.MaxHealth.Value;
                    if (member.RespawnAt > now && (display.RespawnAt == 0 || member.RespawnAt < display.RespawnAt))
                    {
                        display.RespawnAt = member.RespawnAt;
                        display.RespawnDuration = member.RespawnDuration;
                    }
                    if (member.Seen && !member.Alive && member.RespawnAt > now)
                        display.CooldownReducedAt = Math.Max(display.CooldownReducedAt, member.CooldownReducedAt);
                }
                if (display.StrengthImpossible) display.RequiredStrength = null;
                if (display.Alive)
                {
                    display.RespawnAt = display.RespawnDuration = 0;
                    display.CooldownReducedAt = -10;
                }
                result.Add(display);
            }
            foreach (Monster member in Monsters.Values)
            {
                if (groupById.ContainsKey(member.Id)) continue;
                result.Add(new Monster
                {
                    Id = member.Id, Name = member.Name, Tier = member.Tier, Unknown = member.Unknown,
                    Seen = member.Seen, Alive = member.Alive, Health = member.Health, MaxHealth = member.MaxHealth,
                    PlayerClose = member.Seen && member.Alive && member.PlayerClose,
                    PlayerVeryClose = member.Seen && member.Alive && member.PlayerVeryClose,
                    RespawnAt = !member.Alive && member.RespawnAt > now ? member.RespawnAt : 0,
                    RespawnDuration = !member.Alive && member.RespawnAt > now ? member.RespawnDuration : 0,
                    CooldownReducedAt = member.Seen && !member.Alive && member.RespawnAt > now ? member.CooldownReducedAt : -10,
                    RequiredStrength = member.RequiredStrength, StrengthImpossible = member.StrengthImpossible
                });
            }
            // Seen is the UI privacy gate for names, health, timers and count badges.
            result.Sort((left, right) =>
            {
                int tier = (left.Tier ?? int.MaxValue).CompareTo(right.Tier ?? int.MaxValue);
                return tier != 0 ? tier : left.Id.CompareTo(right.Id);
            });
            return result;
        }

        internal bool CanLift(Monster monster)
        {
            return !monster.StrengthImpossible && monster.RequiredStrength.HasValue
                && monster.RequiredStrength.Value <= (Upgrades.TryGetValue("strength", out int strength) ? strength : 0);
        }

        private static double? NormalizeDynamicStrength(JToken valueToken, JToken maximumToken)
        {
            // Unlike health, strength metadata accepts only JSON numeric tokens.
            if (valueToken == null || (valueToken.Type != JTokenType.Integer && valueToken.Type != JTokenType.Float)
                || maximumToken == null || (maximumToken.Type != JTokenType.Integer && maximumToken.Type != JTokenType.Float))
                return null;
            double? value = FiniteNumber(valueToken), forceMax = FiniteNumber(maximumToken);
            if (!value.HasValue || value < 0 || !forceMax.HasValue || forceMax <= 0) return null;
            double threshold = (float)value.Value, maximum = (float)forceMax.Value;
            if (double.IsInfinity(threshold) || double.IsInfinity(maximum) || maximum <= 0) return null;
            double upgrades = Math.Max(0, Math.Ceiling((threshold / maximum - 1) / 0.2));
            if (upgrades > 9007199254740991d) return null;
            // Keep double arithmetic between explicit float32 roundings, as in server.js.
            if (upgrades > 0 && StrengthDisplacement(maximum, upgrades - 1) >= threshold) upgrades--;
            if (StrengthDisplacement(maximum, upgrades) < threshold) upgrades++;
            return upgrades;
        }

        private static double StrengthDisplacement(double maximum, double count)
        {
            return (float)(maximum * (double)(float)(1d + (double)(float)(count * (double)0.2f)));
        }

        private static float? MergeHealth(float? previous, JToken token)
        {
            double? value = FiniteNumber(token);
            if (!value.HasValue || value > float.MaxValue) return previous;
            return (float)Math.Max(0, value.Value);
        }

        private static double? FiniteNumber(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) return null;
            double value;
            string text = token.Type == JTokenType.String ? (string)token : token.ToString(Newtonsoft.Json.Formatting.None);
            if (!double.TryParse(text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out value)
                || double.IsNaN(value) || double.IsInfinity(value)) return null;
            return value;
        }
    }

    // Tick even while the HUD is hidden: suppression must not pause or replay alerts.
    internal sealed class NativeAlertLifetime
    {
        internal const double MaxFreshAge = 15, ShowDuration = 6;
        internal NativeHudState.DestroyedItemAlert Current { get; private set; }
        internal double ShownAt { get; private set; }
        private long consumedSequence;

        internal void Update(NativeHudState.DestroyedItemAlert latest, bool enabled, double now)
        {
            if (latest == null)
            {
                Current = null;
                return;
            }
            if (!enabled)
            {
                consumedSequence = Math.Max(consumedSequence, latest.Sequence);
                Current = null;
                return;
            }
            if (latest.Sequence > consumedSequence)
            {
                consumedSequence = latest.Sequence;
                double age = now - latest.At;
                Current = age >= 0 && age <= MaxFreshAge ? latest : null;
                ShownAt = now;
            }
            if (Current != null && now - ShownAt >= ShowDuration) Current = null;
        }
    }
}
