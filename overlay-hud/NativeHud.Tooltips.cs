using System;
using System.Globalization;
using UnityEngine;

namespace OverlayHUD
{
    internal sealed partial class NativeHud
    {
        private const float TooltipMaxWidth = 360;
        private const float TooltipPadding = 12;
        private const float TooltipLineHeight = 22;
        private const float TooltipGap = 8;
        private RectTransform tooltipRoot;
        private readonly NativeBitmapText[] tooltipLabels = new NativeBitmapText[3];
        private readonly string[] tooltipLines = new string[3];
        private readonly Vector3[] tooltipCorners = new Vector3[4];

        // Call after CreateHeader; the root canvas, not the moving/scaled panel, owns this box.
        private void CreateTooltips()
        {
            if (tooltipRoot != null) return;
            var background = Fill("Tooltip", root.transform, 0, 0, 0, 0,
                new Color32(18, 20, 27, 248), 8);
            tooltipRoot = background.rectTransform;
            var group = tooltipRoot.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;
            for (int i = 0; i < tooltipLabels.Length; i++)
            {
                tooltipLabels[i] = Text(tooltipRoot, robotoBold, i == 0 ? 16 : 14, 0, 0, 0, 0);
                tooltipLabels[i].color = i == 0 ? Color.white : new Color32(214, 220, 230, 255);
                tooltipLabels[i].raycastTarget = false;
            }
            background.raycastTarget = false;
            HideTooltips();
        }

        // Call before every hidden/inactive Tick return, before deactivating the HUD root.
        private void HideTooltips()
        {
            if (tooltipRoot != null) tooltipRoot.gameObject.SetActive(false);
        }

        // Call once per visible Tick, after animations, layout and FitToScreen.
        private void UpdateTooltips()
        {
            // The game's lens distortion invalidates ordinary screen-space hit tests.
            if (gameUIEffectsActive || options == null || !options.Enabled || !options.ShowTooltips
                || root == null || !root.activeSelf || !root.activeInHierarchy
                || state.Hidden || !state.Visible || Cursor.lockState == CursorLockMode.Locked)
            {
                HideTooltips();
                return;
            }

            var canvasRect = root.transform as RectTransform;
            Vector2 mouse = Input.mousePosition;
            // ScreenSpaceOverlay uses a null camera; no raycaster or EventSystem is needed.
            if (canvasRect == null || !RectTransformUtility.RectangleContainsScreenPoint(canvasRect, mouse, null))
            {
                HideTooltips();
                return;
            }

            if (options.ShowUpgrades)
            {
                foreach (var item in headerUpgradeItems)
                {
                    if (item == null || !TooltipContainsMouse(item.Root, mouse)) continue;
                    string key = item.TooltipKey;
                    if (string.IsNullOrEmpty(key) && item.Order >= 0 && item.Order < UpgradeKeys.Length)
                        key = UpgradeKeys[item.Order];
                    if (string.IsNullOrEmpty(key) || (options.HiddenUpgrades?.Contains(key) ?? false)) continue;
                    if (!state.Upgrades.TryGetValue(key, out int count) || count <= 0) continue;
                    tooltipLines[0] = TooltipUpgradeName(key);
                    ShowTooltip(item.Root, canvasRect, 1);
                    return;
                }
            }

            if (options.ShowMonsters)
            {
                foreach (var tile in tiles.Values)
                {
                    var monster = tile.Monster;
                    // Use the displayed snapshot (including grouped HP), never look up a hidden roster entry.
                    if (!tile.Seen || monster == null || !monster.Seen || monster.Unknown
                        || !TooltipContainsMouse(tile.Root, mouse)) continue;
                    tooltipLines[0] = Plugin.LocalizedHudNames.MonsterName(monster.Name, options.Language,
                        tile.Seen && monster.Seen, monster.Unknown);
                    tooltipLines[1] = TooltipHealth(monster);
                    tooltipLines[2] = TooltipStrength(monster);
                    ShowTooltip(tile.Root, canvasRect, 3);
                    return;
                }
            }
            HideTooltips();
        }

        private static bool TooltipContainsMouse(RectTransform target, Vector2 mouse)
        {
            return target != null && target.gameObject.activeInHierarchy
                && RectTransformUtility.RectangleContainsScreenPoint(target, mouse, null);
        }

        private string TooltipHealth(NativeHudState.Monster monster)
        {
            if (!monster.Health.HasValue || !TooltipFinite(monster.Health.Value))
                return options.Translate("ХП: ?", "HP: ?");
            string health = Math.Ceiling(monster.Health.Value).ToString(CultureInfo.InvariantCulture);
            string maximum = monster.MaxHealth.HasValue && TooltipFinite(monster.MaxHealth.Value)
                && monster.MaxHealth.Value > 0
                ? Math.Ceiling(monster.MaxHealth.Value).ToString(CultureInfo.InvariantCulture) : "?";
            return options.Translate("ХП: ", "HP: ") + health + " / " + maximum;
        }

        private string TooltipStrength(NativeHudState.Monster monster)
        {
            if (monster.StrengthImpossible)
                return options.Translate("Сила: нельзя поднять", "Strength: cannot be lifted");
            string strength = monster.RequiredStrength.HasValue && TooltipFinite(monster.RequiredStrength.Value)
                ? monster.RequiredStrength.Value.ToString(CultureInfo.InvariantCulture) : "?";
            return options.Translate("Сила: ", "Strength: ") + strength;
        }

        private static bool TooltipFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        private string TooltipUpgradeName(string key) =>
            Plugin.LocalizedHudNames.UpgradeName(key, options.Language);

        private void ShowTooltip(RectTransform target, RectTransform canvasRect, int lineCount)
        {
            CreateTooltips();
            var bounds = canvasRect.rect;
            float margin = Mathf.Min(6, Mathf.Min(bounds.width, bounds.height) / 4);
            float availableWidth = bounds.width - margin * 2;
            float availableHeight = bounds.height - margin * 2;
            if (availableWidth <= 1 || availableHeight <= 1)
            {
                HideTooltips();
                return;
            }

            float measuredWidth = 0;
            for (int i = 0; i < lineCount; i++)
            {
                SetTooltipFontSize(tooltipLabels[i], i == 0 ? 16 : 14);
                measuredWidth = Mathf.Max(measuredWidth, tooltipLabels[i].Measure(tooltipLines[i]));
            }
            float width = Mathf.Min(TooltipMaxWidth, Mathf.Min(availableWidth, measuredWidth + TooltipPadding * 2));
            float naturalHeight = TooltipPadding * 2 + lineCount * TooltipLineHeight;
            // Shrink padding and line heights too, so even a tiny viewport cannot push the box offscreen.
            float fit = Mathf.Min(1, Mathf.Min(availableHeight / naturalHeight, width / (TooltipPadding * 2 + 1)));
            float padding = TooltipPadding * fit;
            float lineHeight = TooltipLineHeight * fit;
            float height = naturalHeight * fit;
            float textWidth = Mathf.Max(.01f, width - padding * 2);
            for (int i = 0; i < tooltipLabels.Length; i++)
            {
                var label = tooltipLabels[i];
                label.gameObject.SetActive(i < lineCount);
                if (i >= lineCount) { label.Value = ""; continue; }
                float baseSize = i == 0 ? 16 : 14;
                float measured = label.Measure(tooltipLines[i]);
                SetTooltipFontSize(label, baseSize * Mathf.Min(fit, measured > 0 ? textWidth / measured : 1));
                label.Value = tooltipLines[i];
                label.rectTransform.anchoredPosition = new Vector2(padding, -padding - i * lineHeight);
                label.rectTransform.sizeDelta = new Vector2(textWidth, lineHeight);
            }

            // Convert actual on-screen corners, accounting for panel animation, HUD scale and canvas scale.
            target.GetWorldCorners(tooltipCorners);
            Vector2 minimum = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 maximum = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            foreach (var corner in tooltipCorners)
            {
                Vector2 screen = RectTransformUtility.WorldToScreenPoint(null, corner);
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out Vector2 local))
                {
                    HideTooltips();
                    return;
                }
                minimum = Vector2.Min(minimum, local);
                maximum = Vector2.Max(maximum, local);
            }
            float x = (minimum.x + maximum.x) / 2;
            float y = maximum.y + TooltipGap;
            if (y + height > bounds.yMax - margin) y = minimum.y - TooltipGap - height;
            x = Mathf.Clamp(x, bounds.xMin + margin + width / 2, bounds.xMax - margin - width / 2);
            y = Mathf.Clamp(y, bounds.yMin + margin, bounds.yMax - margin - height);
            // Anchoring at the parent's pivot makes anchoredPosition a root-local coordinate.
            tooltipRoot.anchorMin = tooltipRoot.anchorMax = canvasRect.pivot;
            tooltipRoot.pivot = new Vector2(.5f, 0);
            tooltipRoot.sizeDelta = new Vector2(width, height);
            tooltipRoot.anchoredPosition = new Vector2(x, y);
            tooltipRoot.SetAsLastSibling();
            tooltipRoot.gameObject.SetActive(true);
        }

        private static void SetTooltipFontSize(NativeBitmapText label, float size)
        {
            if (label.Size == size) return;
            label.Size = size;
            label.SetVerticesDirty();
        }
    }
}
