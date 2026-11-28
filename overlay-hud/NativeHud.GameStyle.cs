using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OverlayHUD
{
    internal sealed partial class NativeHud
    {
        private FieldInfo haulInstance, inventoryInstance, inventorySpots;
        private float nextStyleLookup;
        private FieldInfo assetManagerInstance;
        private AnimationCurve textWooshIn, textWooshAway;
        private TextMeshProUGUI quotaSource;
        private FieldInfo worldUIInstance;
        private TextMeshProUGUI lossSource;
        private AnimationCurve lossIntro;
        private int previousLoss = -1, lossFrom;
        private bool lossAnimating;
        private double lossLevel;
        private float lossShownAt;
        private static readonly Color GamePriceColor = new Color32(0, 168, 37, 255);
        private static readonly Color GameLossColor = new Color32(241, 51, 55, 255);
        // LoadingUI.levelNumberText on the regular level loading screen.
        private static readonly Color GameLevelColor = new Color32(255, 141, 0, 255);
        // TruckScreenText reveals a letter or a complete emoji at each preset delay.
        private const float TypeDelay = .1f;
        private static readonly string[,] DetectionPhrases =
        {
            { "headman", "eye", "player" },
            { "ghost", "eyesright", "player" },
            { "headman2", "eye", "player" },
            { "headman", "eyesright", "player" },
            { "ghost", "eye", "player" },
            { "headman2", "eyesright", "player" }
        };
        private readonly Rect[,] detectionEmojis = new Rect[DetectionPhrases.GetLength(0), DetectionPhrases.GetLength(1)];
        private static readonly string[] FallbackEmojiNames = { "ghost", "horror", "skull", "dry", "slight_smile" };
        private readonly List<(Rect UV, string Glow)> fallbackPortraits = new List<(Rect, string)>();
        private readonly Dictionary<Rect, RenderTexture> emojiTextures = new Dictionary<Rect, RenderTexture>();
        private readonly Dictionary<NativeBitmapText, TextMeshProUGUI> gameLabels = new Dictionary<NativeBitmapText, TextMeshProUGUI>();
        private Image scanlineSource;
        private RawImage slotSource;
        private TextMeshProUGUI emptySlotNumber;
        private TMP_SpriteAsset taxmanEmojis;
        private Rect fistUV, sadUV, playerUV, moneyUV, lossUV;
        private float nextEmojiLookup;

        private void RefreshGameStyleSources(float now)
        {
            if (!options.GameStyle || now < nextStyleLookup) return;
            nextStyleLookup = now + 1;
            if (textWooshIn == null || textWooshAway == null)
            {
                assetManagerInstance = assetManagerInstance ?? FindGameUIInstanceField("AssetManager");
                var assets = assetManagerInstance?.GetValue(null) as Component;
                if (assets != null)
                {
                    textWooshIn = assets.GetType().GetField("animationCurveWooshIn")?.GetValue(assets) as AnimationCurve;
                    textWooshAway = assets.GetType().GetField("animationCurveWooshAway")?.GetValue(assets) as AnimationCurve;
                }
            }
            if (taxmanEmojis == null && now >= nextEmojiLookup)
            {
                nextEmojiLookup = now + 10;
                foreach (var atlas in Resources.FindObjectsOfTypeAll<TMP_SpriteAsset>())
                {
                    if (atlas.name != "emojis" || atlas.spriteSheet == null) continue;
                    taxmanEmojis = atlas;
                    ClearEmojiTextures();
                    fistUV = EmojiUV(atlas, "fist");
                    fallbackPortraits.Clear();
                    foreach (string name in FallbackEmojiNames)
                    {
                        Rect uv = EmojiUV(atlas, name);
                        if (uv.width > 0) fallbackPortraits.Add((uv, "glow-taxman-" + name + ".png"));
                    }
                    for (int row = 0; row < DetectionPhrases.GetLength(0); row++)
                        for (int i = 0; i < DetectionPhrases.GetLength(1); i++)
                            detectionEmojis[row, i] = EmojiUV(atlas, DetectionPhrases[row, i]);
                    sadUV = EmojiUV(atlas, ":'(");
                    playerUV = EmojiUV(atlas, "player");
                    moneyUV = EmojiUV(atlas, "$$");
                    lossUV = EmojiUV(atlas, "burnmoney");
                    break;
                }
            }
            if (quotaSource == null)
            {
                haulInstance = haulInstance ?? FindGameUIInstanceField("HaulUI");
                var haul = haulInstance?.GetValue(null) as Component;
                if (haul != null)
                {
                    quotaSource = haul.GetComponent<TextMeshProUGUI>();
                    scanlineSource = haul.transform.Find("Scanlines")?.GetComponent<Image>();
                }
            }
            if (slotSource == null) FindInventorySlot();
            if (lossSource == null)
            {
                worldUIInstance = worldUIInstance ?? FindGameUIInstanceField("WorldSpaceUIParent");
                var owner = worldUIInstance?.GetValue(null) as Component;
                var prefab = owner?.GetType().GetField("valueLostPrefab")?.GetValue(owner) as GameObject;
                if (prefab != null)
                {
                    lossSource = prefab.GetComponent<TextMeshProUGUI>();
                    var effect = prefab.GetComponent("WorldSpaceUIValueLost");
                    lossIntro = effect?.GetType().GetField("curveIntro")?.GetValue(effect) as AnimationCurve;
                }
            }
            if (slotSource != null && emptySlotNumber == null)
            {
                var spot = slotSource.GetComponent("InventorySpot");
                emptySlotNumber = spot?.GetType().GetField("noItem")?.GetValue(spot) as TextMeshProUGUI;
            }
        }

        private const float LossRollDuration = .48f;

        private int RollingLoss(float now)
        {
            double t = Mathf.SmoothStep(0, 1, (now - lossShownAt) / LossRollDuration);
            return lossFrom + (int)Math.Round((previousLoss - lossFrom) * t);
        }

        private void ObserveLoss(float now)
        {
            bool visible = options.GameStyle && options.ShowLostValue && options.Enabled && state.Visible && !state.Hidden && state.HasMapValue;
            if (!visible || !options.Animations || lossLevel != state.StartedAt || state.LostValue < previousLoss)
            {
                if (lossAnimating) FinishLossAnimation();
            }
            else if (previousLoss >= 0 && state.LostValue > previousLoss)
            {
                lossFrom = lossAnimating ? RollingLoss(now) : previousLoss;
                lossShownAt = now;
                lossAnimating = true;
            }
            previousLoss = visible ? state.LostValue : -1;
            lossLevel = state.StartedAt;
        }

        private void AnimateLoss(float now)
        {
            if (!lossAnimating) return;
            float age = now - lossShownAt;
            if (age >= LossRollDuration)
            {
                FinishLossAnimation();
                return;
            }
            var label = headerLoss.Number;
            var text = headerLoss.Price;
            string amount = FormatPrice(true, RollingLoss(now));
            var color = Color.Lerp(Color.white, GameLossColor, age / .18f);
            label.Value = amount;
            label.color = color;
            if (text == null) return;
            text.text = amount;
            text.color = color;
            float intro = Mathf.Clamp01(age * 10);
            float pulse = 1 + .10f * (lossIntro?.Evaluate(intro) ?? intro) * (1 - age / LossRollDuration);
            text.transform.localScale = Vector3.one * pulse;
        }

        private void FinishLossAnimation()
        {
            lossAnimating = false;
            var amount = FormatPrice(true, state.LostValue);
            headerLoss.Number.Value = amount;
            headerLoss.Number.color = GameLossColor;
            if (headerLoss.Price == null) return;
            headerLoss.Price.text = amount;
            headerLoss.Price.color = GameLossColor;
            headerLoss.Price.transform.localScale = Vector3.one;
        }

        private void FindInventorySlot()
        {
            inventoryInstance = inventoryInstance ?? FindGameUIInstanceField("Inventory");
            var inventory = inventoryInstance?.GetValue(null) as Component;
            if (inventory == null) return;
            inventorySpots = inventorySpots ?? inventory.GetType().GetField("inventorySpots",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (!(inventorySpots?.GetValue(inventory) is IList spots)) return;
            foreach (Component spot in spots)
            {
                if (spot == null) continue;
                slotSource = spot.GetComponent<RawImage>();
                if (slotSource != null) break;
            }
        }

        private bool GameHeaderReady => options.GameStyle && quotaSource != null && quotaSource.font != null;
        private Color GameSlotColor => slotSource != null ? slotSource.color : new Color(0, 0, 0, .55f);

        private static Rect EmojiUV(TMP_SpriteAsset atlas, string name)
        {
            int index = atlas.GetSpriteIndexFromName(name);
            if (index < 0) return UnityEngine.Rect.zero;
            var glyph = atlas.spriteCharacterTable[index].glyph.glyphRect;
            var sheet = atlas.spriteSheet;
            return new Rect((float)glyph.x / sheet.width, (float)glyph.y / sheet.height,
                (float)glyph.width / sheet.width, (float)glyph.height / sheet.height);
        }

        private RenderTexture EmojiTexture(Rect uv)
        {
            var sheet = taxmanEmojis.spriteSheet;
            if (!emojiTextures.TryGetValue(uv, out var texture))
            {
                // Filter each glyph independently so its mip levels never contain neighbouring emoji.
                texture = new RenderTexture(Mathf.RoundToInt(uv.width * sheet.width), Mathf.RoundToInt(uv.height * sheet.height), 0)
                {
                    name = "OverlayHUD Taxman Emoji",
                    useMipMap = true,
                    autoGenerateMips = true,
                    filterMode = FilterMode.Trilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
                emojiTextures.Add(uv, texture);
            }
            if (!texture.IsCreated())
            {
                var previous = RenderTexture.active;
                try { Graphics.Blit(sheet, texture, uv.size, uv.position); }
                finally { RenderTexture.active = previous; }
            }
            return texture;
        }

        private void ClearEmojiTextures()
        {
            foreach (var texture in emojiTextures.Values) UnityEngine.Object.Destroy(texture);
            emojiTextures.Clear();
        }

        // SemiUI.CalculateSpringOffset, shared by InventorySpot.UpdateUI and ItemInfoUI.ItemInfoText.
        private static float GameSpring(float age, float amount, float frequency, float duration)
        {
            if (age < 0 || age >= duration) return 0;
            float t = age / duration;
            return amount * (1 - t) * Mathf.Sin(2 * Mathf.PI * frequency * (1 - t) * t);
        }

        private float GameTextEnterY(float age, float height)
        {
            float t = Mathf.Clamp01(age * 4);
            float progress = textWooshIn?.Evaluate(t) ?? Mathf.SmoothStep(0, 1, t);
            // Limit the slide to part of one line so neighbouring rows stay readable.
            return (progress - 1) * height * .32f + GameSpring(age, 5, 5, .3f);
        }

        private float GameTextExitY(float progress, float height)
        {
            float t = Mathf.Clamp01(progress);
            return -height * .32f * (textWooshAway?.Evaluate(t) ?? Mathf.SmoothStep(0, 1, t));
        }

        internal bool AnimateTextEntrance(RectTransform motion, float age)
        {
            if (!options.GameStyle || !options.Animations || age >= .3f) return false;
            motion.anchoredPosition = new Vector2(0, GameTextEnterY(age, motion.rect.height));
            motion.localScale = Vector3.one * (1 + GameSpring(age, .1f, 2.5f, .2f));
            return true;
        }

        private static Shadow AddGameShadow(Graphic graphic)
        {
            var shadow = graphic.gameObject.AddComponent<Shadow>();
            shadow.effectColor = Color.black;
            shadow.effectDistance = new Vector2(0, -2);
            return shadow;
        }

        private float HeaderTextWidth(NativeBitmapText label, string value, bool upgradeCount = false)
        {
            gameLabels.TryGetValue(label, out var text);
            float width = StyleText(label, value, ref text, teko, useScanlines: false,
                fixedWidth: label == headerTimer && options.GameStyle ? GameLevelTimerWidth : 0, upgradeCount: upgradeCount);
            if (text != null) gameLabels[label] = text;
            return width;
        }

        private float StyleText(NativeBitmapText label, string value, ref TextMeshProUGUI text, NativeFontAtlas classicAtlas,
            bool useScanlines, bool boldBadge = false, bool compactBold = false, float fixedWidth = 0, bool upgradeCount = false, bool alert = false)
        {
            bool active = GameHeaderReady;
            var font = active ? quotaSource.font : null;
            bool level = label == headerLevelCaption || label == headerLevelNumber;
            bool compact = level || label == headerTimer || compactBold || upgradeCount;
            bool bold = compact || boldBadge;
            // Some game fonts lack localized captions. Preserve readable glyphs in that case.
            if (active && !alert)
                foreach (char ch in value)
                    if (!font.HasCharacter(ch, true, true)) { active = false; break; }
            label.enabled = !active;
            if (text != null) text.gameObject.SetActive(active);
            if (!active)
            {
                var gameAtlas = bold ? robotoBold : robotoRegular;
                SetHeaderFont(label, options.GameStyle ? gameAtlas : classicAtlas, label.Size);
                return fixedWidth > 0 ? fixedWidth : label.Measure(value);
            }
            if (text == null)
            {
                var visualParent = label.rectTransform;
                if (!alert)
                {
                    visualParent = StretchStyleRect("TextMotion", visualParent);
                    visualParent.pivot = new Vector2(.5f, .5f);
                    visualParent.offsetMin = visualParent.offsetMax = Vector2.zero;
                }
                var rect = StretchStyleRect("GameText", visualParent);
                text = rect.gameObject.AddComponent<TextMeshProUGUI>();
                if (!alert) rect.gameObject.AddComponent<NativeTextEntrance>().Initialize(this, visualParent);
                text.raycastTarget = false;
                text.alignment = TextAlignmentOptions.Capline;
                text.textWrappingMode = TextWrappingModes.NoWrap;
                text.richText = false;
                if (useScanlines)
                    StretchStyleRect("Scanlines", rect).gameObject.AddComponent<Image>().raycastTarget = false;
            }
            text.font = font;
            // HaulUI flashes its instance material, including _FaceColor and _GlowColor.
            // The font asset's base material is independent of the animated quota text.
            text.fontSharedMaterial = lossSource != null && lossSource.font == font ? lossSource.fontSharedMaterial : font.material;
            text.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            text.fontWeight = alert || upgradeCount ? FontWeight.Bold : FontWeight.Regular;
            // TMP adds the font asset's bold spacing on top of characterSpacing.
            text.characterSpacing = quotaSource.characterSpacing - (compact ? font.boldSpacing : 0) - (alert || upgradeCount ? 2 : 0);
            text.wordSpacing = quotaSource.wordSpacing;
            if (text.fontFeatures != quotaSource.fontFeatures)
                text.fontFeatures = quotaSource.fontFeatures;
            // Size the visible capitals, independent of the quota font's tall line box.
            var face = font.faceInfo;
            float capHeight = label.Size * .80f;
            if (label == headerLevelCaption) capHeight = 17;
            if (label == headerLevelNumber) capHeight = 31;
            text.fontSize = capHeight * face.pointSize / Mathf.Max(1, face.capLine * face.scale);
            text.color = label.color;
            if (useScanlines)
            {
                var lines = text.transform.GetChild(0).GetComponent<Image>();
                ApplyScanlines(lines, text.color);
            }
            if (text.text != value) text.text = value;
            return fixedWidth > 0 ? fixedWidth : Mathf.Ceil(text.GetPreferredValues(value).x) + 3;
        }

        private void ApplyScanlines(Image lines, Color tint)
        {
            lines.enabled = options.GameStyle && scanlineSource != null;
            if (!lines.enabled) return;
            lines.sprite = scanlineSource.sprite;
            lines.type = scanlineSource.type;
            lines.material = scanlineSource.material;
            tint.a = scanlineSource.color.a;
            lines.color = tint;
        }

        private string FormatPrice(bool loss, int amount) => (loss ? "-$" : "$") + amount.ToString("N0",
            System.Globalization.CultureInfo.GetCultureInfo(options.Language == "en" ? "en-US" : "ru-RU"));

        private void LayoutGamePrice(HeaderItem item, int amount, string caption)
        {
            bool loss = item == headerLoss;
            string value = FormatPrice(loss, amount);
            var uv = loss ? lossUV : moneyUV;
            bool hasEmoji = taxmanEmojis != null && uv.width > 0;
            if (item.Icon == null)
            {
                item.Icon = Image("CurrencyEmoji", item.Root, 0, 0, 32, 32);
                AddGameShadow(item.Icon);
            }
            item.Icon.enabled = hasEmoji;
            item.Caption.gameObject.SetActive(!hasEmoji);
            float labelWidth = 32;
            if (hasEmoji)
            {
                item.Icon.texture = taxmanEmojis.spriteSheet;
                item.Icon.uvRect = uv;
            }
            else
            {
                SetHeaderCaption(item.Caption, caption, 22);
                labelWidth = item.Caption.Measure(caption);
            }
            const float height = 34;
            SetHeaderFont(item.Number, robotoBold, headerTimer.Size);
            item.Number.Value = value;
            item.Number.color = loss ? GameLossColor : GamePriceColor;
            float width = StyleText(item.Number, value, ref item.Price, teko, useScanlines: false, compactBold: true);
            if (hasEmoji) SetHeaderRect(item.Icon.rectTransform, 0, (height - 32) / 2, 32, 32);
            else SetHeaderRect(item.Caption.rectTransform, 0, 0, labelWidth, height);
            SetHeaderRect(item.Number.rectTransform, labelWidth + 3, 0, width, height);
            item.Root.sizeDelta = new Vector2(labelWidth + 3 + width, height);
        }

        private static RectTransform StretchStyleRect(string name, RectTransform parent)
        {
            var rect = Rect(name, parent, 0, 0, 0, 0);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return rect;
        }

        private void RefreshGameSlot(Tile tile)
        {
            bool active = options.GameStyle;
            if (active && tile.Slot == null)
            {
                tile.Slot = Image("InventorySlot", tile.Content, 0, 0, CardSize, CardSize);
                tile.Slot.transform.SetSiblingIndex(tile.Background.transform.GetSiblingIndex() + 1);
                tile.SlotTier = Image("Tier", tile.Content, 0, 0, 21, 3);
            }
            if (tile.Slot == null) return;
            tile.Slot.enabled = active;
            tile.SlotTier.enabled = active;
            tile.Border.enabled = tile.Background.enabled = !active;
            float scale = active ? .97f : 1f;
            tile.ArtMask.localScale = new Vector3(scale, scale, 1);
            float inset = CardBorder + ArtworkSize * (1 - scale) / 2;
            tile.ArtMask.anchoredPosition = new Vector2(inset, -inset);
            bool unknown = active && !tile.Monster.Seen;
            if (unknown)
            {
                if (tile.UnknownLabel == null)
                {
                    var rect = Rect("UnknownMonster", tile.Content, 0, 0, CardSize, CardSize);
                    rect.SetSiblingIndex(tile.ArtMask.GetSiblingIndex());
                    tile.UnknownLabel = rect.gameObject.AddComponent<TextMeshProUGUI>();
                    tile.UnknownLabel.raycastTarget = false;
                    tile.UnknownLabel.text = "?";
                    tile.UnknownLabel.textWrappingMode = TextWrappingModes.NoWrap;
                    tile.UnknownLabel.horizontalAlignment = HorizontalAlignmentOptions.Geometry;
                    tile.UnknownLabel.verticalAlignment = VerticalAlignmentOptions.Geometry;
                    tile.UnknownLabel.fontSize = CardSize;
                }
                var label = tile.UnknownLabel;
                label.font = emptySlotNumber != null ? emptySlotNumber.font : quotaSource != null ? quotaSource.font : TMP_Settings.defaultFontAsset;
                if (label.font != null)
                    label.fontSharedMaterial = emptySlotNumber != null ? emptySlotNumber.fontSharedMaterial : label.font.material;
                label.color = emptySlotNumber != null ? emptySlotNumber.color : new Color32(94, 94, 94, 255);
                label.fontStyle = emptySlotNumber != null ? emptySlotNumber.fontStyle : FontStyles.Bold;
                label.fontWeight = emptySlotNumber != null ? emptySlotNumber.fontWeight : FontWeight.Regular;
            }
            if (tile.UnknownLabel != null) tile.UnknownLabel.enabled = unknown;
            if (!active) return;
            // Inventory Spot uses an untextured RawImage tinted translucent black.
            tile.Slot.texture = slotSource != null ? slotSource.texture : null;
            tile.Slot.uvRect = slotSource != null ? slotSource.uvRect : new Rect(0, 0, 1, 1);
            tile.Slot.material = slotSource != null ? slotSource.material : null;
            tile.Slot.color = GameSlotColor;
        }
    }
}
