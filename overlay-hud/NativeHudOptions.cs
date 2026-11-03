using System;
using System.Collections.Generic;

namespace OverlayHUD
{
    internal sealed class NativeHudOptions
    {
        internal float Scale = 1, OffsetX = 0, OffsetY = 20, MonsterSize = 70, UpgradeSize = 38;
        internal int Columns = 4, HorizontalColumns = 11, UpgradeRows = 2;
        internal bool HorizontalLayout = true, Enabled = true;
        internal bool ShowLevel = true, ShowTimer = true, ShowMapValue = true, ShowLostValue = true;
        internal bool ShowMonsters = true, ShowProximityWaves = true, ShowRespawnIndicator = true;
        internal bool ShowDestroyedItemAlerts = true, ShowHealth = true, ShowRespawnTimer = true;
        internal bool ShowStrength = true, ShowUpgrades = true, ShowMonsterCounts = true;
        internal bool ShowDamageNumbers = true, Animations = true;
        internal bool OnlyAliveMonsters = false, FilterUndetectedMonsters = false;
        internal bool WrapValueLabels = true, ShowTooltips = false;
        internal string Language = "ru", Alignment = "Center";
        internal HashSet<string> HiddenUpgrades = new HashSet<string>(StringComparer.Ordinal);

        internal string Translate(string ru, string en) => Language == "en" ? en : ru;

        internal bool ShouldDisplayMonster(NativeHudState.Monster monster) => ShowMonsters
            && (!OnlyAliveMonsters || monster.Alive || (!monster.Seen && !FilterUndetectedMonsters));
    }
}
