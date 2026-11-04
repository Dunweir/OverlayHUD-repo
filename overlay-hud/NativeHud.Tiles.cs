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
        private const float CooldownTop = CardSize * .07f + 3;

        private const float CooldownPillAlpha = .64f;

        private sealed class Tile
        {
            internal NativeHudState.Monster Monster;
            internal RectTransform Root, Content, CountBacking, StrengthBacking, LabelBacking, DamageBacking;
            internal RawImage Icon, PreviousIcon, Glow, PreviousGlow, StrengthIcon;
            internal CanvasGroup Group, CountGroup, StrengthGroup, HealthGroup, CooldownGroup, DamageGroup;
            internal NativeRoundedPanel Bar, Flash, TimerPill;
            internal NativeOutline Border;
            internal NativeOutline[] Waves;
            internal readonly float[] WaveStarted = { -100, -100 };
            internal readonly float[] WaveDuration = { 4.8f, 4.8f };
            internal NativeCooldownSector Cooldown;
            internal NativeShimmer Shimmer;
            internal NativeBitmapText Label, Count, Strength, Damage;
            internal Color GlowColor;
            internal Vector2 Position, TargetPosition;
            internal float EnteredAt, RevealedAt = -10, DamagedAt = -10, HealthChangedAt;
            internal float? Health;
            internal float HealthRatio, PreviousHealthRatio, TargetHealthRatio, HealthAlpha, CooldownAlpha;
            internal bool Seen, HasHealth, HasCooldown, PlayerClose, PlayerVeryClose;
            internal double RespawnAt, RespawnDuration, CooldownReducedAt;
            internal string Name;
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
            var shadow = Image("Shadow", tile.Content, -17.5f, -12.5f, 105, 105);
            shadow.texture = Texture("card-shadow.png");
            shadow.color = new Color(0, 0, 0, .28f);
            tile.Waves = new NativeOutline[2];
            for (int i = 0; i < tile.Waves.Length; i++)
            {
                var wave = Rect("ProximityWave", tile.Content, 0, 0, CardSize, CardSize).gameObject.AddComponent<NativeOutline>();
                wave.raycastTarget = false;
                wave.color = Color.clear;
                tile.Waves[i] = wave;
            }
            Fill("Card", tile.Content, 0, 0, CardSize, CardSize, new Color32(7, 9, 12, 255), CardSize * .16f);
            // Glow must remain above the opaque background and outside the artwork stencil.
            float haloSize = ArtworkSize * 1.5f;
            float haloOffset = (CardSize - haloSize) / 2;
            tile.PreviousGlow = Image("PreviousGlow", tile.Content, haloOffset, haloOffset, haloSize, haloSize);
            tile.Glow = Image("Glow", tile.Content, haloOffset, haloOffset, haloSize, haloSize);
            var mask = Fill("ArtworkMask", tile.Content, CardBorder, CardBorder, ArtworkSize, ArtworkSize, Color.white, CardSize * .16f - CardBorder);
            mask.gameObject.AddComponent<Mask>().showMaskGraphic = false;
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
            tile.HealthGroup = track.gameObject.AddComponent<CanvasGroup>();
            tile.Bar = Fill("Health", track.transform, 2, 2, 68, HealthHeight - 4, Color.green);
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

        private void UpdateTile(Tile tile, NativeHudState.Monster monster, float now, bool created)
        {
            tile.Monster = monster;
            string name = monster.Seen ? monster.Name : "unknown";
            if (tile.Name != name || tile.Icon.texture == null)
            {
                tile.PreviousIcon.texture = tile.Icon.texture;
                tile.PreviousGlow.texture = tile.Glow.texture;
                tile.PreviousIcon.enabled = tile.PreviousGlow.enabled = tile.Icon.texture != null;
                tile.Name = name;
                tile.Icon.texture = Icon(name);
                tile.Glow.texture = Texture("glow-" + tile.Icon.texture.name);
                tile.RevealedAt = created ? -10 : now;
                tile.Shimmer.Source = (Texture2D)tile.Icon.texture;
                tile.Shimmer.SetAllDirty();
                tile.Shimmer.enabled = monster.Seen && tile.Icon.texture.name == "new.png";
            }
            if (monster.Seen && tile.Seen && monster.Health.HasValue && tile.Health.HasValue && monster.Health.Value < tile.Health.Value)
            {
                tile.DamagedAt = now;
                string damage = "-" + Math.Max(1, Math.Ceiling(tile.Health.Value - monster.Health.Value)).ToString(CultureInfo.InvariantCulture) + " HP";
                tile.Damage.Value = damage;
                float width = tile.Damage.Measure(damage);
                tile.Damage.rectTransform.sizeDelta = new Vector2(width, 18.2f);
                tile.DamageBacking.sizeDelta = new Vector2(width + 18, 27.2f);
            }
            tile.Seen = monster.Seen;
            tile.Health = monster.Health;
            tile.GlowColor = TierColor(monster.Tier);
            tile.Border.color = tile.GlowColor;
            tile.CountBacking.gameObject.SetActive(options.ShowMonsterCounts && monster.Seen && monster.Count > 1);
            string count = "x" + monster.Count.ToString(CultureInfo.InvariantCulture);
            tile.Count.Value = count;
            float countWidth = Mathf.Max(24, tile.Count.Measure(count));
            tile.Count.rectTransform.sizeDelta = new Vector2(countWidth, 13);
            tile.CountBacking.sizeDelta = new Vector2(countWidth + 12, 18);
            tile.StrengthBacking.gameObject.SetActive(showStrength && monster.Seen);
            bool knownStrength = monster.StrengthImpossible || monster.RequiredStrength.HasValue;
            tile.StrengthIcon.enabled = knownStrength;
            string strength = monster.StrengthImpossible ? "X" : monster.RequiredStrength?.ToString("0", CultureInfo.InvariantCulture) ?? "?";
            tile.Strength.Value = strength;
            tile.Strength.color = !monster.RequiredStrength.HasValue || monster.StrengthImpossible ? new Color32(255, 178, 0, 255)
                : state.CanLift(monster) ? new Color32(76, 175, 80, 255) : new Color32(255, 68, 68, 255);
            float strengthX = knownStrength ? 23 : 7;
            float strengthWidth = Mathf.Min(CardSize + 10 - strengthX - 7, Mathf.Max(knownStrength ? 8 : 24, tile.Strength.Measure(strength)));
            tile.Strength.rectTransform.anchoredPosition = new Vector2(strengthX, -4);
            tile.Strength.rectTransform.sizeDelta = new Vector2(strengthWidth, 15);
            tile.StrengthBacking.sizeDelta = new Vector2(strengthX + strengthWidth + 7, 22);

            bool hasHealth = monster.Health.HasValue && monster.MaxHealth > 0;
            tile.HasHealth = showHealth && monster.Seen && hasHealth;
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
            tile.PlayerClose = monster.Seen && monster.Alive && (monster.PlayerClose || monster.PlayerVeryClose);
            tile.PlayerVeryClose = monster.PlayerVeryClose;
        }

        private static float Ease(float t) => NativeHudLayout.Ease(t, .22f, .7f, .25f, 1);
        private static Color HealthColor(float ratio)
        {
            switch (NativeHudLayout.HealthBand(ratio))
            {
                case 0: return new Color32(255, 63, 53, 255);
                case 1: return new Color32(255, 189, 53, 255);
                case 2: return new Color32(241, 211, 59, 255);
                default: return new Color32(51, 212, 99, 255);
            }
        }

        private void Animate(float now)
        {
            float dt = Time.unscaledDeltaTime;
            foreach (var tile in tiles.Values)
            {
                float enter = options.Animations ? NativeHudLayout.Ease((now - tile.EnteredAt) / .36f, .2f, .75f, .25f, 1) : 1;
                float reveal = options.Animations ? Ease((now - tile.RevealedAt) / .72f) : 1;
                tile.Group.alpha = enter;
                tile.Position = Vector2.Lerp(tile.Position, tile.TargetPosition, options.Animations ? 1 - Mathf.Exp(-dt * 18) : 1);
                tile.Root.anchoredPosition = tile.Position + new Vector2(0, -8 * (1 - enter));
                ScaleFromCenter(tile.Content, Mathf.Lerp(.94f, 1, enter));
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

                float detail = options.Animations ? Ease((now - tile.RevealedAt - .2f) / .38f) : 1;
                tile.CountGroup.alpha = tile.StrengthGroup.alpha = detail;
                float detailScale = Mathf.Lerp(.9f, 1, detail);
                tile.CountBacking.localScale = tile.StrengthBacking.localScale = new Vector3(detailScale, detailScale, 1);
                tile.CountBacking.anchoredPosition = new Vector2(CardSize + 4 - tile.CountBacking.sizeDelta.x, -CardSize - 4 + 18 - 5 * (1 - detail));
                tile.StrengthBacking.anchoredPosition = new Vector2(-4, -CardSize - 4 + 22 - 5 * (1 - detail));

                float healthT = options.Animations ? Ease((now - tile.HealthChangedAt) / .16f) : 1;
                tile.HealthRatio = Mathf.Lerp(tile.PreviousHealthRatio, tile.TargetHealthRatio, healthT);
                tile.Bar.rectTransform.sizeDelta = new Vector2(68 * tile.HealthRatio, HealthHeight - 4);
                tile.Bar.color = Color.Lerp(HealthColor(tile.PreviousHealthRatio), HealthColor(tile.TargetHealthRatio), healthT);
                tile.HealthAlpha = options.Animations ? Mathf.MoveTowards(tile.HealthAlpha, tile.HasHealth ? 1 : 0, dt / .18f) : tile.HasHealth ? 1 : 0;
                tile.HealthGroup.alpha = tile.HealthAlpha;
                var track = (RectTransform)tile.HealthGroup.transform;
                track.localScale = new Vector3(1, Mathf.Lerp(.55f, 1, tile.HealthAlpha), 1);
                track.anchoredPosition = new Vector2(-1, -CardSize - 14 + HealthHeight + 4 * (1 - tile.HealthAlpha));

                AnimateCooldown(tile, now, dt);
                AnimateDamage(tile, now);
                AnimateWaves(tile, now);
            }
        }

        private void AnimateCooldown(Tile tile, float now, float dt)
        {
            bool active = tile.HasCooldown && tile.RespawnAt > now;
            tile.CooldownAlpha = options.Animations ? Mathf.MoveTowards(tile.CooldownAlpha, active ? 1 : 0, dt / .22f) : active ? 1 : 0;
            tile.Cooldown.Remaining = NativeHudLayout.CooldownFraction(now, tile.RespawnAt, tile.RespawnDuration);
            tile.Cooldown.color = new Color(0, 0, 0, options.ShowRespawnIndicator ? .78f * tile.CooldownAlpha : 0);
            tile.CooldownGroup.alpha = showRespawn ? tile.CooldownAlpha : 0;
            int remaining = (int)Math.Ceiling(Math.Max(0, tile.RespawnAt - now));
            string text = (remaining / 60).ToString(CultureInfo.InvariantCulture) + ":" + (remaining % 60).ToString("00", CultureInfo.InvariantCulture);
            tile.Label.Value = text;
            float width = tile.Label.Measure(text);
            tile.Label.rectTransform.sizeDelta = new Vector2(width, 14.7f);
            tile.LabelBacking.sizeDelta = new Vector2(width + 10, 18.7f);
            float reduction = Mathf.Clamp01((float)(now - tile.CooldownReducedAt) / 1.05f);
            float pulse = !options.Animations ? 0 : reduction < .16f ? reduction / .16f : 1 - (reduction - .16f) / .84f;
            tile.TimerPill.color = Color.Lerp(new Color(0, 0, 0, CooldownPillAlpha), new Color(.74f, .043f, .039f, .96f), pulse);
            float scale = Mathf.Lerp(.9f, 1, tile.CooldownAlpha) * (1 + .14f * pulse);
            tile.LabelBacking.localScale = new Vector3(scale, scale, 1);
            tile.LabelBacking.anchoredPosition = new Vector2((CardSize - (width + 10) * scale) / 2,
                -CooldownTop + 3 * (1 - tile.CooldownAlpha) + pulse);
        }

        private void AnimateDamage(Tile tile, float now)
        {
            float t = options.Animations ? Mathf.Clamp01(now - tile.DamagedAt) : 1;
            float flash = t < .1f ? t / .1f : t < .48f ? Mathf.Lerp(1, .72f, (t - .1f) / .38f) : .72f * (1 - t) / .52f;
            tile.Flash.color = new Color(1, .149f, .149f, .46f * flash);
            tile.DamageGroup.alpha = !options.ShowDamageNumbers ? 0 : t < .12f ? t / .12f : t < .58f ? 1 : (1 - t) / .42f;
            float scale = t < .12f ? Mathf.Lerp(.82f, 1, t / .12f) : Mathf.Lerp(1, 1.04f, (t - .12f) / .88f);
            tile.DamageBacking.localScale = new Vector3(scale, scale, 1);
            tile.DamageBacking.anchoredPosition = new Vector2((CardSize - tile.DamageBacking.sizeDelta.x * scale) / 2,
                -CardSize / 2 + tile.DamageBacking.sizeDelta.y * Mathf.Lerp(.5f, .98f, t));
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
