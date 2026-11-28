using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace OverlayHUD
{
    internal sealed partial class NativeHud
    {
        private const float CardSize = NativeHudLayout.MonsterSize;
        private const float CardBorder = NativeHudLayout.BorderWidth;
        private const float ArtworkSize = CardSize - 2 * CardBorder;
        private const float HealthHeight = CardSize * .145f;
        private const float BatteryHeight = 8, BatteryTop = 4, BatteryPitch = BatteryHeight + 4;
        private const float CooldownTop = CardSize * .07f + 3;
        private const float GameTileTextSize = 21;
        private const float AlertEyeSize = 32;
        private const float EmojiStep = 23;
        private const float AlertEyeGap = 3;

        private const float CooldownPillAlpha = .64f;

        private sealed class Tile
        {
            internal NativeHudState.Monster Monster;
            internal RectTransform Root, Content, CountBacking, StrengthBacking, LabelBacking, DamageBacking;
            internal RawImage Icon, PreviousIcon, Glow, PreviousGlow, StrengthIcon, Shadow;
            internal Texture2D Portrait;
            internal CanvasGroup Group, CountGroup, StrengthGroup, HealthGroup, CooldownGroup, DamageGroup;
            internal NativeRoundedPanel HealthTrack, Bar, Flash, TimerPill;
            internal TMPro.TextMeshProUGUI GameTimer, GameCount, GameStrength, GameDamage;
            internal TMPro.TextMeshProUGUI TimerSource, UnknownLabel;
            internal int DisplayedSeconds = -1;
            internal bool SquareIndicators;
            internal GameObject ClassicEye, DetectionMark;
            internal readonly RawImage[] DetectionIcons = new RawImage[3];
            internal int DetectionPhrase;
            internal bool WasDetected;
            internal float PhraseTime;
            internal NativeRoundedPanel VisionTrack, VisionFill;
            internal CanvasGroup VisionGroup, AlertEyeGroup;
            internal float VisionRatio;
            internal RawImage VisionGlow;
            internal Shadow FistShadow;
            internal NativeBatteryFrame HealthCells, VisionCells;
            internal NativeOutline Border;
            internal RawImage Slot, SlotTier;
            internal NativeRoundedPanel Background;
            internal RectTransform ArtMask;
            internal NativeOutline[] Waves;
            internal readonly float[] WaveStarted = { -100, -100 };
            internal readonly float[] WaveDuration = { 4.8f, 4.8f };
            internal NativeCooldownSector Cooldown;
            internal NativeShimmer Shimmer;
            internal NativeBitmapText Label, Count, Strength, Damage;
            internal Color GlowColor;
            internal Vector2 Position, TargetPosition;
            internal float EnteredAt, RevealedAt = -10, SlotChangedAt = -10, DamagedAt = -10, HealthChangedAt;
            internal float? Health;
            internal float HealthRatio, PreviousHealthRatio, TargetHealthRatio, HealthAlpha, CooldownAlpha;
            internal bool Seen, HasHealth, HasCooldown, PlayerClose, PlayerVeryClose;
            internal double RespawnAt, RespawnDuration, CooldownReducedAt;
            internal string Name;
            internal bool LegacyIcons;
        }

        private static NativeRoundedPanel Fill(string name, Transform parent, float x, float y, float w, float h, Color color, float radius = 999)
        {
            var fill = Rect(name, parent, x, y, w, h).gameObject.AddComponent<NativeRoundedPanel>();
            fill.Radius = radius;
            fill.color = color;
            fill.raycastTarget = false;
            return fill;
        }

        private static RawImage Image(string name, Transform parent, float x, float y, float w, float h)
        {
            var image = Rect(name, parent, x, y, w, h).gameObject.AddComponent<RawImage>();
            image.raycastTarget = false;
            return image;
        }

        private static void ScaleFromCenter(RectTransform rect, float scale)
        {
            rect.localScale = new Vector3(scale, scale, 1);
            rect.anchoredPosition = new Vector2(rect.sizeDelta.x * (1 - scale) / 2, -rect.sizeDelta.y * (1 - scale) / 2);
        }

        private Tile CreateTile(float now)
        {
            var tile = new Tile { EnteredAt = now };
            tile.Root = Rect("Monster", panel, 0, 0, CardSize, CardSize);
            tile.Group = tile.Root.gameObject.AddComponent<CanvasGroup>();
            tile.Content = Rect("Content", tile.Root, 0, 0, CardSize, CardSize);
            tile.Shadow = Image("Shadow", tile.Content, -17.5f, -12.5f, 105, 105);
            tile.Shadow.texture = Texture("card-shadow.png");
            tile.Shadow.color = new Color(0, 0, 0, .28f);
            tile.Waves = new NativeOutline[2];
            for (int i = 0; i < tile.Waves.Length; i++)
            {
                var wave = Rect("ProximityWave", tile.Content, 0, 0, CardSize, CardSize).gameObject.AddComponent<NativeOutline>();
                wave.raycastTarget = false;
                wave.color = Color.clear;
                tile.Waves[i] = wave;
            }
            tile.Background = Fill("Card", tile.Content, 0, 0, CardSize, CardSize, new Color32(7, 9, 12, 255), CardSize * .16f);
            // Glow must remain above the opaque background and outside the artwork stencil.
            float haloSize = ArtworkSize * 1.5f;
            float haloOffset = (CardSize - haloSize) / 2;
            tile.PreviousGlow = Image("PreviousGlow", tile.Content, haloOffset, haloOffset, haloSize, haloSize);
            tile.Glow = Image("Glow", tile.Content, haloOffset, haloOffset, haloSize, haloSize);
            var mask = Fill("ArtworkMask", tile.Content, CardBorder, CardBorder, ArtworkSize, ArtworkSize, Color.white, CardSize * .16f - CardBorder);
            mask.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            tile.ArtMask = mask.rectTransform;
            tile.PreviousIcon = Image("PreviousIcon", mask.transform, 0, 0, ArtworkSize, ArtworkSize);
            tile.Icon = Image("Icon", mask.transform, 0, 0, ArtworkSize, ArtworkSize);
            tile.PreviousIcon.enabled = tile.PreviousGlow.enabled = false;
            tile.Shimmer = Rect("UnknownShimmer", mask.transform, 0, 0, ArtworkSize, ArtworkSize).gameObject.AddComponent<NativeShimmer>();
            tile.Shimmer.raycastTarget = false;
            tile.Shimmer.color = new Color32(255, 221, 139, 255);
            tile.Shimmer.enabled = false;
            tile.Cooldown = Rect("CooldownSector", mask.transform, 0, 0, ArtworkSize, ArtworkSize).gameObject.AddComponent<NativeCooldownSector>();
            tile.Cooldown.raycastTarget = false;
            tile.Cooldown.color = new Color(0, 0, 0, .78f);
            tile.Border = Rect("Border", tile.Content, 0, 0, CardSize, CardSize).gameObject.AddComponent<NativeOutline>();
            tile.Border.raycastTarget = false;
            tile.Flash = Fill("DamageFlash", tile.Content, 0, 0, CardSize, CardSize, Color.clear, CardSize * .16f);

            var track = Fill("HealthTrack", tile.Content, -1, CardSize + 14 - HealthHeight, 72, HealthHeight, new Color(0, 0, 0, .76f));
            tile.HealthTrack = track;
            tile.HealthGroup = track.gameObject.AddComponent<CanvasGroup>();
            tile.Bar = Fill("Health", track.transform, 2, 2, 68, HealthHeight - 4, Color.green);
            var cyan = new Color32(106, 211, 255, 255);
            tile.VisionTrack = Fill("VisionTrack", tile.Content, -1, CardSize + 16, 72, HealthHeight, new Color(0, 0, 0, .76f));
            tile.VisionGlow = Image("SightGlow", tile.VisionTrack.transform, -8, -8, 88, HealthHeight + 16);
            tile.VisionGlow.texture = Texture("sight-glow.png");
            tile.VisionGlow.enabled = false;
            tile.VisionGroup = tile.VisionTrack.gameObject.AddComponent<CanvasGroup>();
            tile.VisionGroup.alpha = 0;
            tile.VisionFill = Fill("Detection", tile.VisionTrack.transform, 2, 2, 0, HealthHeight - 4, cyan);
            var rim = Fill("EyeRim", tile.VisionTrack.transform, -2, (HealthHeight - 15) / 2, 20, 15, new Color32(13, 19, 24, 255));
            var eye = Fill("Eye", rim.transform, 2, 2, 16, 11, Color.white);
            Fill("Pupil", eye.transform, 5, 2, 6, 7, Color.black);
            tile.ClassicEye = rim.gameObject;
            var alertEye = Rect("AlertEye", tile.VisionTrack.transform, 0, HealthHeight + AlertEyeGap, 72, AlertEyeSize);
            tile.AlertEyeGroup = alertEye.gameObject.AddComponent<CanvasGroup>();
            tile.AlertEyeGroup.alpha = 0;
            var mark = Fill("DetectionMark", alertEye, (72 - 14) / 2, (AlertEyeSize - 18) / 2, 14, 18, new Color32(14, 17, 20, 255), 0);
            Fill("Stem", mark.transform, 5, 2, 4, 8, Color.white, 0);
            Fill("Dot", mark.transform, 5, 12, 4, 4, Color.white, 0);
            tile.DetectionMark = mark.gameObject;
            tile.DetectionMark.SetActive(false);
            for (int i = 0; i < tile.DetectionIcons.Length; i++)
            {
                float x = (72 - AlertEyeSize) / 2 + (i - 1) * EmojiStep;
                tile.DetectionIcons[i] = Image("DetectionEmoji", alertEye, x, 0, AlertEyeSize, AlertEyeSize);
                AddGameShadow(tile.DetectionIcons[i]);
                tile.DetectionIcons[i].enabled = false;
            }
            tile.TimerPill = Fill("CooldownPill", tile.Content, 6, CooldownTop, 58, 18.7f, new Color(0, 0, 0, CooldownPillAlpha));
            tile.LabelBacking = tile.TimerPill.rectTransform;
            tile.CooldownGroup = tile.LabelBacking.gameObject.AddComponent<CanvasGroup>();
            tile.Label = Text(tile.LabelBacking, roboto, CardSize * .21f, 5, 2, 48, 14.7f);
            tile.Label.Centered = true;

            var textShadow = tile.Label.gameObject.AddComponent<Shadow>();
            textShadow.effectColor = Color.black;
            textShadow.effectDistance = new Vector2(0, -1);

            tile.CountBacking = Fill("CountPill", tile.Content, 38, 56, 36, 18, new Color(0, 0, 0, .82f)).rectTransform;
            tile.CountGroup = tile.CountBacking.gameObject.AddComponent<CanvasGroup>();
            tile.Count = Text(tile.CountBacking, robotoBold, 13, 6, 3, 24, 13);
            tile.Count.Centered = true;
            tile.StrengthBacking = Fill("StrengthPill", tile.Content, -4, 52, 48, 22, new Color(0, 0, 0, .82f)).rectTransform;
            tile.StrengthGroup = tile.StrengthBacking.gameObject.AddComponent<CanvasGroup>();
            tile.StrengthIcon = Image("StrengthIcon", tile.StrengthBacking, 7, 3, 16, 16);
            tile.StrengthIcon.texture = Texture("strength-emoji.png");
            tile.Strength = Text(tile.StrengthBacking, robotoBold, 15, 23, 4, 18, 15);
            tile.Strength.Centered = true;
            tile.DamageBacking = Fill("DamagePill", tile.Content, 0, 20, 70, 27.2f, new Color(0, 0, 0, .86f)).rectTransform;
            tile.DamageGroup = tile.DamageBacking.gameObject.AddComponent<CanvasGroup>();
            tile.Damage = Text(tile.DamageBacking, roboto, CardSize * .26f, 9, 5, 52, 18.2f);
            tile.Damage.Centered = true;
            tile.Damage.color = new Color32(255, 222, 219, 255);
            return tile;
        }

        private static void SetPanelRadius(NativeRoundedPanel panel, float radius)
        {
            panel.Radius = radius;
            panel.SetVerticesDirty();
        }

        private void UpdateTile(Tile tile, NativeHudState.Monster monster, float now, bool created)
        {
            tile.Monster = monster;
            RefreshGameSlot(tile);
            if (tile.SquareIndicators != options.GameStyle)
            {
                tile.SquareIndicators = options.GameStyle;
                // Classic clips the sector to the inset portrait. Game covers the entire inventory slot.
                var sector = tile.Cooldown.rectTransform;
                sector.SetParent(options.GameStyle ? tile.Content : tile.ArtMask, false);
                if (options.GameStyle) sector.SetSiblingIndex(tile.ArtMask.GetSiblingIndex() + 1);
                float side = options.GameStyle ? CardSize : ArtworkSize;
                SetHeaderRect(sector, 0, 0, side, side);
                tile.DamageBacking.GetComponent<NativeRoundedPanel>().enabled = !options.GameStyle;
                tile.Damage.color = options.GameStyle ? GameLossColor : new Color32(255, 222, 219, 255);
                tile.ClassicEye.SetActive(!options.GameStyle);
                SetHeaderFont(tile.Count, robotoBold, options.GameStyle ? 19 : 13);
                SetHeaderFont(tile.Strength, robotoBold, options.GameStyle ? 19 : 15);
                SetHeaderFont(tile.Damage, options.GameStyle ? robotoBold : roboto, options.GameStyle ? GameTileTextSize : CardSize * .26f);
                float radius = options.GameStyle ? 0 : 999;
                float cardRadius = options.GameStyle ? 0 : CardSize * .16f;
                SetPanelRadius(tile.Flash, cardRadius);
                SetPanelRadius(tile.ArtMask.GetComponent<NativeRoundedPanel>(), Mathf.Max(0, cardRadius - CardBorder));
                tile.Shadow.enabled = !options.GameStyle;
                tile.CountBacking.GetComponent<NativeRoundedPanel>().enabled = !options.GameStyle;
                SetPanelRadius(tile.StrengthBacking.GetComponent<NativeRoundedPanel>(), radius);
                tile.Cooldown.ShowHand = options.GameStyle;
                tile.Cooldown.SetVerticesDirty();
                if (options.GameStyle && tile.FistShadow == null)
                    tile.FistShadow = AddGameShadow(tile.StrengthIcon);
                tile.FistShadow.enabled = options.GameStyle;
                SetPanelRadius(tile.HealthTrack, radius);
                SetPanelRadius(tile.Bar, radius);
                SetPanelRadius(tile.VisionTrack, radius);
                SetPanelRadius(tile.VisionFill, radius);
                if (options.GameStyle && tile.HealthCells == null)
                {
                    tile.HealthCells = StretchStyleRect("BatteryCells", tile.HealthTrack.rectTransform).gameObject.AddComponent<NativeBatteryFrame>();
                    tile.VisionCells = StretchStyleRect("BatteryCells", tile.VisionTrack.rectTransform).gameObject.AddComponent<NativeBatteryFrame>();
                    tile.HealthCells.raycastTarget = tile.VisionCells.raycastTarget = false;
                    AddGameShadow(tile.HealthCells);
                    AddGameShadow(tile.VisionCells);
                }
                tile.HealthCells.enabled = tile.VisionCells.enabled = options.GameStyle;
                tile.HealthTrack.enabled = tile.Bar.enabled = tile.VisionTrack.enabled = tile.VisionFill.enabled = !options.GameStyle;
                var barSize = options.GameStyle ? new Vector2(CardSize, BatteryHeight) : new Vector2(72, HealthHeight);
                tile.HealthTrack.rectTransform.sizeDelta = tile.VisionTrack.rectTransform.sizeDelta = barSize;
                SetHeaderRect((RectTransform)tile.AlertEyeGroup.transform, (barSize.x - 72) / 2,
                    barSize.y + AlertEyeGap, 72, AlertEyeSize);
                tile.TimerPill.enabled = !options.GameStyle;
                tile.DisplayedSeconds = -1;
                foreach (var wave in tile.Waves)
                {
                    wave.Radius = cardRadius;
                    wave.SetVerticesDirty();
                }
            }
            string name = monster.Seen ? monster.Name : "unknown";
            if (created || monster.Seen != tile.Seen) tile.SlotChangedAt = now;
            if (tile.Name != name || tile.Portrait == null || tile.LegacyIcons != options.LegacyIcons)
            {
                tile.Name = name;
                tile.LegacyIcons = options.LegacyIcons;
                tile.Portrait = Icon(name);
            }
            bool missingPortrait = monster.Seen && tile.Portrait.name == "new.png";
            bool useEmoji = missingPortrait && options.GameStyle && taxmanEmojis != null && fallbackPortraits.Count > 0;
            var emoji = useEmoji ? fallbackPortraits[(monster.Id & int.MaxValue) % fallbackPortraits.Count] : default;
            Texture artwork = useEmoji ? (Texture)EmojiTexture(emoji.UV) : tile.Portrait;
            if (tile.Icon.texture != artwork)
            {
                tile.PreviousIcon.texture = tile.Icon.texture;
                tile.PreviousGlow.texture = tile.Glow.texture;
                tile.PreviousIcon.enabled = tile.Icon.enabled && tile.Icon.texture != null;
                tile.Icon.texture = artwork;
                tile.Glow.texture = Texture(useEmoji ? emoji.Glow : "glow-" + tile.Portrait.name);
                tile.RevealedAt = created ? -10 : now;
                tile.Shimmer.Source = tile.Portrait;
                tile.Shimmer.SetAllDirty();
                tile.Shimmer.enabled = missingPortrait && !useEmoji;
            }
            tile.Icon.enabled = !options.GameStyle || monster.Seen;
            if (!tile.Icon.enabled) tile.PreviousIcon.enabled = false;
            tile.Glow.enabled = tile.Icon.enabled && tile.Glow.texture != null;
            tile.PreviousGlow.enabled = tile.PreviousIcon.enabled && tile.PreviousGlow.texture != null;
            if (monster.Seen && tile.Seen && monster.Health.HasValue && tile.Health.HasValue && monster.Health.Value < tile.Health.Value)
            {
                tile.DamagedAt = now;
                string damage = "-" + Math.Max(1, Math.Ceiling(tile.Health.Value - monster.Health.Value)).ToString(CultureInfo.InvariantCulture) + " HP";
                tile.Damage.Value = damage;
                if (options.GameStyle && tile.GameDamage != null) tile.GameDamage.GetComponent<NativeTextEntrance>().Play();
                float width = StyleText(tile.Damage, damage, ref tile.GameDamage, roboto, useScanlines: false, compactBold: true);
                float padding = options.GameStyle ? 0 : 9;
                float height = options.GameStyle ? GameTileTextSize : 18.2f;
                SetHeaderRect(tile.Damage.rectTransform, padding, options.GameStyle ? 0 : 5, width, height);
                tile.DamageBacking.sizeDelta = new Vector2(width + 2 * padding, options.GameStyle ? height : 27.2f);
            }
            tile.Seen = monster.Seen;
            tile.Health = monster.Health;
            tile.GlowColor = TierColor(monster.Tier);
            tile.Border.color = tile.GlowColor;
            if (tile.SlotTier != null) tile.SlotTier.color = tile.GlowColor;
            if (created) tile.DetectionPhrase = (monster.Id & int.MaxValue) % DetectionPhrases.GetLength(0);
            bool showCount = options.ShowMonsterCounts && monster.Seen && monster.Count > 1;
            tile.CountBacking.gameObject.SetActive(showCount);
            if (showCount)
            {
                string count = "x" + monster.Count.ToString(CultureInfo.InvariantCulture);
                tile.Count.Value = count;
                float width = StyleText(tile.Count, count, ref tile.GameCount, robotoBold, useScanlines: false, boldBadge: true);
                float padding = options.GameStyle ? 3 : 6;
                width = options.GameStyle ? width : Mathf.Max(24, width);
                SetHeaderRect(tile.Count.rectTransform, padding, options.GameStyle ? 2 : 3, width, tile.Count.Size);
                tile.CountBacking.sizeDelta = new Vector2(width + padding * 2, tile.Count.Size + 5);
            }
            tile.StrengthBacking.gameObject.SetActive(showStrength && monster.Seen);
            bool knownStrength = monster.StrengthImpossible || monster.RequiredStrength.HasValue;
            tile.StrengthIcon.enabled = knownStrength;
            bool hasFist = options.GameStyle && taxmanEmojis != null && fistUV.width > 0;
            tile.StrengthIcon.texture = hasFist ? (Texture)EmojiTexture(fistUV) : Texture("strength-emoji.png");
            float iconSize = hasFist ? 32 : 16;
            SetHeaderRect(tile.StrengthIcon.rectTransform, hasFist ? -4 : 7, (22 - iconSize) / 2, iconSize, iconSize);
            string strength = monster.StrengthImpossible ? "X" : monster.RequiredStrength?.ToString("0", CultureInfo.InvariantCulture) ?? "?";
            tile.Strength.Value = strength;
            tile.Strength.color = !monster.RequiredStrength.HasValue || monster.StrengthImpossible ? new Color32(255, 178, 0, 255)
                : state.CanLift(monster) ? new Color32(76, 175, 80, 255) : new Color32(255, 68, 68, 255);
            float strengthX = hasFist && knownStrength ? 27 : knownStrength ? 23 : 7;
            float measured = StyleText(tile.Strength, strength, ref tile.GameStrength, robotoBold, useScanlines: false, boldBadge: true);
            float strengthWidth = Mathf.Min(CardSize + 10 - strengthX - 7, Mathf.Max(knownStrength ? 8 : 24, measured));
            SetHeaderRect(tile.Strength.rectTransform, strengthX, options.GameStyle ? 1.5f : 4, strengthWidth, tile.Strength.Size);
            tile.StrengthBacking.sizeDelta = new Vector2(strengthX + strengthWidth + 7, 22);

            bool hasHealth = monster.Health.HasValue && monster.MaxHealth > 0;
            tile.HasHealth = showHealth && monster.Seen && monster.Alive && hasHealth;
            float ratio = hasHealth ? Mathf.Clamp01(monster.Health.Value / monster.MaxHealth.Value) : 0;
            if (created) tile.HealthRatio = tile.TargetHealthRatio = ratio;
            if (tile.TargetHealthRatio != ratio)
            {
                tile.PreviousHealthRatio = tile.HealthRatio;
                tile.TargetHealthRatio = ratio;
                tile.HealthChangedAt = now;
            }
            else if (created) tile.PreviousHealthRatio = ratio;
            tile.HasCooldown = monster.Seen && !monster.Alive && monster.RespawnAt > now;
            tile.RespawnAt = monster.RespawnAt;
            tile.RespawnDuration = monster.RespawnDuration;
            tile.CooldownReducedAt = monster.CooldownReducedAt;
            tile.PlayerClose = monster.Alive && (monster.PlayerClose || monster.PlayerVeryClose);
            tile.PlayerVeryClose = monster.PlayerVeryClose;
        }

        private static float Ease(float t) => NativeHudLayout.Ease(t, .22f, .7f, .25f, 1);
        private Color HealthColor(float ratio)
        {
            switch (NativeHudLayout.HealthBand(ratio))
            {
                case 0: return options.GameStyle ? GameLossColor : new Color32(255, 63, 53, 255);
                case 1: return new Color32(255, 189, 53, 255);
                case 2: return new Color32(241, 211, 59, 255);
                default: return options.GameStyle ? GamePriceColor : new Color32(51, 212, 99, 255);
            }
        }

        private void Animate(float now)
        {
            float dt = Time.unscaledDeltaTime;
            foreach (var tile in tiles.Values)
            {
                bool classicAnimation = options.Animations && !options.GameStyle;
                float enter = classicAnimation ? NativeHudLayout.Ease((now - tile.EnteredAt) / .36f, .2f, .75f, .25f, 1) : 1;
                float reveal = classicAnimation ? Ease((now - tile.RevealedAt) / .72f) : 1;
                tile.Group.alpha = enter;
                tile.Position = Vector2.Lerp(tile.Position, tile.TargetPosition, options.Animations ? 1 - Mathf.Exp(-dt * 18) : 1);
                tile.Root.anchoredPosition = tile.Position + new Vector2(0, -8 * (1 - enter));
                float slotScale = options.GameStyle && options.Animations
                    ? 1 + GameSpring(now - tile.SlotChangedAt, .5f, 2, .2f) : Mathf.Lerp(.94f, 1, enter);
                ScaleFromCenter(tile.Content, slotScale);
                tile.Icon.color = new Color(1, 1, 1, reveal);
                tile.PreviousIcon.color = new Color(1, 1, 1, 1 - reveal);
                ScaleFromCenter(tile.Icon.rectTransform, Mathf.Lerp(.96f, 1, reveal));
                ScaleFromCenter(tile.PreviousIcon.rectTransform, Mathf.Lerp(1, .98f, reveal));
                var glow = tile.GlowColor;
                glow.a = reveal;
                tile.Glow.color = glow;
                glow.a = 1 - reveal;
                tile.PreviousGlow.color = glow;
                if (reveal >= 1) tile.PreviousIcon.enabled = tile.PreviousGlow.enabled = false;
                if (tile.Shimmer.enabled) tile.Shimmer.Phase = options.Animations ? Mathf.Repeat(now, 8) / 8 : 0;

                float detail = classicAnimation ? Ease((now - tile.RevealedAt - .2f) / .38f) : 1;
                tile.CountGroup.alpha = tile.StrengthGroup.alpha = detail;
                float detailScale = Mathf.Lerp(.9f, 1, detail);
                tile.CountBacking.localScale = tile.StrengthBacking.localScale = new Vector3(detailScale, detailScale, 1);
                tile.CountBacking.anchoredPosition = new Vector2(CardSize + (options.GameStyle ? 0 : 4) - tile.CountBacking.sizeDelta.x,
                    (options.GameStyle ? 0 : -CardSize - 4 + 18) - 5 * (1 - detail));
                tile.StrengthBacking.anchoredPosition = new Vector2(options.GameStyle ? 0 : -4,
                    -CardSize - (options.GameStyle ? 0 : 4) + 22 - 5 * (1 - detail));

                float healthT = options.Animations ? Ease((now - tile.HealthChangedAt) / .16f) : 1;
                tile.HealthRatio = Mathf.Lerp(tile.PreviousHealthRatio, tile.TargetHealthRatio, healthT);
                tile.Bar.rectTransform.sizeDelta = new Vector2(68 * tile.HealthRatio, HealthHeight - 4);
                tile.Bar.color = Color.Lerp(HealthColor(tile.PreviousHealthRatio), HealthColor(tile.TargetHealthRatio), healthT);
                if (options.GameStyle) tile.HealthCells.SetFill(tile.HealthRatio, tile.Bar.color);
                tile.HealthAlpha = options.Animations ? Mathf.MoveTowards(tile.HealthAlpha, tile.HasHealth ? 1 : 0, dt / .18f) : tile.HasHealth ? 1 : 0;
                tile.HealthGroup.alpha = tile.HealthAlpha;
                var track = (RectTransform)tile.HealthGroup.transform;
                track.localScale = new Vector3(1, Mathf.Lerp(.55f, 1, tile.HealthAlpha), 1);
                track.anchoredPosition = new Vector2(options.GameStyle ? 0 : -1,
                    -CardSize - (options.GameStyle ? BatteryTop : 14 - HealthHeight) + 4 * (1 - tile.HealthAlpha));

                AnimateVision(tile, dt);
                AnimateCooldown(tile, now, dt);
                AnimateDamage(tile, now);
                AnimateWaves(tile, now);
            }
        }

        private void AnimateVision(Tile tile, float dt)
        {
            int progress = tile.Monster.Detection;
            float targetAlpha = progress > 0 ? 1 : 0;
            float alpha = options.Animations ? Mathf.MoveTowards(tile.VisionGroup.alpha, targetAlpha, dt / .2f) : targetAlpha;
            tile.VisionGroup.alpha = alpha;
            bool detected = options.GameStyle && progress == 100;
            if (!options.GameStyle || alpha == 0 || (progress > 0 && progress < 100))
                tile.AlertEyeGroup.alpha = 0;
            else if (detected)
            {
                if (!tile.WasDetected)
                {
                    tile.DetectionPhrase = (tile.DetectionPhrase + 1) % DetectionPhrases.GetLength(0);
                    tile.PhraseTime = 0;
                }
                else tile.PhraseTime += dt;
                tile.AlertEyeGroup.alpha = 1;
                int phrase = tile.DetectionPhrase;
                bool hasPhrase = taxmanEmojis != null && detectionEmojis[phrase, 0].width > 0
                    && detectionEmojis[phrase, 1].width > 0 && detectionEmojis[phrase, 2].width > 0;
                tile.DetectionMark.SetActive(!hasPhrase);
                for (int i = 0; i < tile.DetectionIcons.Length; i++)
                {
                    var icon = tile.DetectionIcons[i];
                    icon.enabled = hasPhrase && (!options.Animations || tile.PhraseTime >= i * TypeDelay);
                    if (!icon.enabled) continue;
                    icon.texture = EmojiTexture(detectionEmojis[phrase, i]);
                }
            }
            tile.WasDetected = detected;
            if (alpha == 0)
            {
                tile.VisionRatio = 0;
                return;
            }
            var track = tile.VisionTrack.rectTransform;
            track.localScale = new Vector3(1, Mathf.Lerp(.55f, 1, alpha), 1);
            float top = options.GameStyle ? BatteryTop + (showHealth ? BatteryPitch : 0) : (showHealth ? 16 : 4);
            track.anchoredPosition = new Vector2(options.GameStyle ? 0 : -1, -CardSize - top + 3 * (1 - alpha));
            if (progress == 0) return; // Retain the last fill and color throughout fade-out.
            bool alert = progress == 100;
            tile.VisionGlow.enabled = alert && !options.GameStyle;
            if (tile.VisionGlow.enabled)
            {
                float pulse = options.Animations ? .72f + .25f * Mathf.Cos(Time.unscaledTime * 2 * Mathf.PI / 1.3f) : .72f;
                var glow = new Color(1, .13f, .09f, pulse);
                tile.VisionGlow.color = glow;
            }
            float target = progress / 100f;
            tile.VisionRatio = options.Animations ? Mathf.Lerp(tile.VisionRatio, target, 1 - Mathf.Exp(-dt / .13f)) : target;
            tile.VisionFill.rectTransform.sizeDelta = new Vector2(68 * tile.VisionRatio, HealthHeight - 4);
            Color color = new Color32(106, 211, 255, 255);
            if (alert) color = options.GameStyle ? GameLossColor : new Color32(255, 63, 53, 255);
            tile.VisionFill.color = options.Animations ? Color.Lerp(tile.VisionFill.color, color, 1 - Mathf.Exp(-dt * 22)) : color;
            if (options.GameStyle) tile.VisionCells.SetFill(tile.VisionRatio, tile.VisionFill.color);
        }

        private void AnimateCooldown(Tile tile, float now, float dt)
        {
            bool active = tile.HasCooldown && tile.RespawnAt > now;
            bool textWasVisible = tile.CooldownGroup.alpha > 0;
            tile.CooldownAlpha = options.Animations ? Mathf.MoveTowards(tile.CooldownAlpha, active ? 1 : 0, dt / .22f) : active ? 1 : 0;
            tile.Cooldown.Remaining = NativeHudLayout.CooldownFraction(now, tile.RespawnAt, tile.RespawnDuration);
            Color sectorColor = Color.black;
            sectorColor.a = options.ShowRespawnIndicator ? tile.CooldownAlpha * (options.GameStyle ? .80f : .78f) : 0;
            tile.Cooldown.color = sectorColor;
            tile.CooldownGroup.alpha = showRespawn ? tile.CooldownAlpha : 0;
            if (tile.CooldownGroup.alpha <= 0) return;
            if (!textWasVisible && options.GameStyle && tile.GameTimer != null)
                tile.GameTimer.GetComponent<NativeTextEntrance>().Play();
            int remaining = (int)Math.Ceiling(Math.Max(0, tile.RespawnAt - now));
            var source = GameHeaderReady ? quotaSource : null;
            if (tile.DisplayedSeconds != remaining || !ReferenceEquals(tile.TimerSource, source))
            {
                tile.DisplayedSeconds = remaining;
                tile.TimerSource = source;
                string text = (remaining / 60).ToString(CultureInfo.InvariantCulture) + ":" + (remaining % 60).ToString("00", CultureInfo.InvariantCulture);
                tile.Label.Value = text;
                SetHeaderFont(tile.Label, roboto, options.GameStyle ? GameTileTextSize : CardSize * .21f);
                float width = StyleText(tile.Label, text, ref tile.GameTimer, roboto, useScanlines: true,
                    compactBold: true, fixedWidth: options.GameStyle ? CardSize - 10 : 0);
                float height = options.GameStyle ? GameTileTextSize : 14.7f;
                tile.Label.rectTransform.sizeDelta = new Vector2(width, height);
                tile.LabelBacking.sizeDelta = new Vector2(width + 10, height + 4);
            }
            float reduction = Mathf.Clamp01((float)(now - tile.CooldownReducedAt) / 1.05f);
            float pulse = !options.Animations ? 0 : reduction < .16f ? reduction / .16f : 1 - (reduction - .16f) / .84f;
            if (options.GameStyle && tile.GameTimer != null)
                tile.GameTimer.color = Color.Lerp(tile.Label.color, new Color(1f, .39f, .37f), pulse);
            else
                tile.TimerPill.color = Color.Lerp(new Color(0, 0, 0, CooldownPillAlpha), new Color(.74f, .043f, .039f, .96f), pulse);
            float scale = Mathf.Lerp(.9f, 1, tile.CooldownAlpha) * (1 + .14f * pulse);
            tile.LabelBacking.localScale = new Vector3(scale, scale, 1);
            float exitY = options.GameStyle && options.Animations && !active
                ? GameTextExitY(1 - tile.CooldownAlpha, GameTileTextSize) : 0;
            tile.LabelBacking.anchoredPosition = new Vector2((CardSize - tile.LabelBacking.sizeDelta.x * scale) / 2,
                (options.GameStyle ? -CardSize - 7 : -CooldownTop) + 3 * (1 - tile.CooldownAlpha) + pulse + exitY);
        }

        private static float GameDamageOffset(bool health, bool detection, bool alertEye, bool respawn)
        {
            float offset = health ? BatteryTop + BatteryHeight + 4 : 3;
            if (detection)
                offset = BatteryTop + (health ? BatteryPitch : 0) + BatteryHeight + (alertEye ? AlertEyeGap + AlertEyeSize : 0) + 4;
            return respawn ? Mathf.Max(offset, 38) : offset;
        }

        private void AnimateDamage(Tile tile, float now)
        {
            float t = options.Animations ? Mathf.Clamp01(now - tile.DamagedAt) : 1;
            float flash = t < .1f ? t / .1f : t < .48f ? Mathf.Lerp(1, .72f, (t - .1f) / .38f) : .72f * (1 - t) / .52f;
            tile.Flash.color = new Color(1, .149f, .149f, .46f * flash);
            tile.DamageGroup.alpha = !options.ShowDamageNumbers ? 0 : t < .12f ? t / .12f : t < .58f ? 1 : (1 - t) / .42f;
            float scale = t < .12f ? Mathf.Lerp(.82f, 1, t / .12f) : Mathf.Lerp(1, 1.04f, (t - .12f) / .88f);
            float y = -CardSize / 2 + tile.DamageBacking.sizeDelta.y * Mathf.Lerp(.5f, .98f, t);
            if (options.GameStyle)
            {
                if (!options.Animations)
                    tile.DamageGroup.alpha = options.ShowDamageNumbers && now - tile.DamagedAt < 1 ? 1 : 0;
                scale = Mathf.Min(1, CardSize / Mathf.Max(1, tile.DamageBacking.sizeDelta.x));
                bool sight = tile.VisionGroup.alpha > 0;
                float offset = GameDamageOffset(sight ? showHealth : tile.HealthAlpha > 0, sight, tile.AlertEyeGroup.alpha > 0, tile.CooldownGroup.alpha > 0);
                y = -CardSize - offset;
                if (options.Animations && t > .75f) y += GameTextExitY((t - .75f) * 4, GameTileTextSize);
            }
            tile.DamageBacking.localScale = new Vector3(scale, options.GameStyle ? 1 : scale, 1);
            tile.DamageBacking.anchoredPosition = new Vector2((CardSize - tile.DamageBacking.sizeDelta.x * scale) / 2, y);
        }

        private void AnimateWaves(Tile tile, float now)
        {
            if (!options.ShowProximityWaves || !options.Animations)
            {
                for (int i = 0; i < tile.Waves.Length; i++)
                {
                    tile.Waves[i].color = Color.clear;
                    tile.WaveStarted[i] = -100;
                }
                return;
            }
            for (int i = 0; i < tile.Waves.Length; i++)
            {
                float elapsed = now - tile.WaveStarted[i];
                float duration = tile.PlayerVeryClose ? 1.15f : 4.8f;
                if (tile.PlayerClose && elapsed < tile.WaveDuration[i] && duration != tile.WaveDuration[i])
                {
                    // Change urgency without restarting the visible ring.
                    float phase = elapsed / tile.WaveDuration[i];
                    tile.WaveDuration[i] = duration;
                    tile.WaveStarted[i] = now - phase * duration;
                    elapsed = phase * duration;
                }
                if (elapsed >= tile.WaveDuration[i] && tile.PlayerClose)
                {
                    bool starting = now - tile.WaveStarted[1 - i] >= tile.WaveDuration[1 - i];
                    tile.WaveDuration[i] = duration;
                    tile.WaveStarted[i] = now - (starting && i == 0 ? duration / 2 : 0);
                    elapsed = now - tile.WaveStarted[i];
                }
                float t = Mathf.Clamp01(elapsed / tile.WaveDuration[i]);
                float ease = NativeHudLayout.Ease(t, .18f, .62f, .32f, 1);
                var color = tile.GlowColor;
                color.a = .8f * (1 - ease);
                tile.Waves[i].color = color;
                ScaleFromCenter(tile.Waves[i].rectTransform, 1 + .4125f * ease);
                if (t < 1) tile.Waves[i].SetVerticesDirty();
            }
        }
    }
}
