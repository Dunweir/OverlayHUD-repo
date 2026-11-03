using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace OverlayHUD
{
    internal sealed partial class NativeHud
    {

        private const float HeaderUpgradeGap = 7.22f;
        private const float HeaderPillGap = 10;
        private RectTransform headerPanel, headerSummaryPanel, headerUpgradePanel;
        private HeaderItem headerLevel, headerMap, headerLoss;
        private NativeBitmapText headerLevelCaption, headerLevelNumber, headerTimer;
        private RectTransform headerTimerSeparator;
        private readonly HeaderItem[] headerUpgradeItems = new HeaderItem[12];
        private static readonly string[] UpgradeKeys =
        {
            "strength", "tumbleLaunch", "range", "sprintSpeed", "tumbleWings", "crouchRest",
            "extraJump", "tumbleClimb", "health", "stamina", "mapPlayerCount", "deathHeadBattery"
        };
        private static readonly string[] UpgradeFiles =
        {
            "Strength_Upgrade", "Tumble_Launch_Upgrade", "Range_Upgrade", "Sprint_Speed_Upgrade",
            "Tumble_Wings_Upgrade", "Crouch_Rest_Upgrade", "Extra_Jump_Upgrade", "Tumble_Climb_Upgrade",
            "Upgrade_Health_Albedo", "Stamina_Upgrade", "Upgrade_Map-Player-Count_Albedo", "Death_Head_Battery_Upgrade"
        };

        private sealed class HeaderItem
        {
            internal RectTransform Root;
            internal NativeBitmapText Caption, Number;
            internal RawImage Icon;
            internal string TooltipKey;
            internal int Order, Count;
            internal Vector2 TargetPosition;
            internal bool Positioned;
            internal float Width => Root.sizeDelta.x * Root.localScale.x;
            internal float Height => Root.sizeDelta.y * Root.localScale.y;
        }

        private sealed class HeaderRow
        {
            internal readonly List<HeaderItem> Items = new List<HeaderItem>();
            internal float Width, Height;
        }

        private void CreateHeader()
        {
            headerPanel = Rect("Header", panel, 0, 0, 0, 0);
            headerSummaryPanel = Rect("LevelAndCurrency", headerPanel, 0, 0, 0, 0);
            headerUpgradePanel = Rect("Upgrades", headerPanel, 0, 0, 0, 0);
            Color amber = HeaderColor(255, 178, 0);
            headerLevel = CreateHeaderPill("Level", headerSummaryPanel, 53, HeaderColor(255, 178, 0, .16f), 6);
            headerLevelCaption = CreateHeaderText(headerLevel.Root, 28, "LEVEL", amber);
            headerLevelNumber = CreateHeaderText(headerLevel.Root, 48, "0", amber);
            headerTimer = CreateHeaderText(headerLevel.Root, 32, "00:00", HeaderColor(247, 251, 255));
            headerTimerSeparator = Rect("TimerSeparator", headerLevel.Root, 0, 4, 1, 46);
            var separator = headerTimerSeparator.gameObject.AddComponent<RawImage>();
            separator.color = HeaderColor(255, 178, 0, .32f);
            separator.raycastTarget = false;
            headerMap = CreateHeaderCurrency("MapValue", "MAP", HeaderColor(45, 190, 115, .16f), HeaderColor(156, 255, 192));
            headerLoss = CreateHeaderCurrency("LostValue", "LOSS", HeaderColor(225, 70, 70, .17f), HeaderColor(255, 154, 154));

            for (int i = 0; i < UpgradeKeys.Length; i++)
            {
                HeaderUpgradeColors(UpgradeKeys[i], out Color foreground, out Color background);
                float size = Mathf.Clamp(options.UpgradeSize, 20, 64);
                var item = CreateHeaderPill(UpgradeKeys[i], headerUpgradePanel, size, background, 5);
                item.Order = i;
                item.TooltipKey = UpgradeKeys[i];
                item.Icon = Rect("Icon", item.Root, 0, 0, size * .82f, size)
                    .gameObject.AddComponent<RawImage>();
                item.Icon.texture = Texture("upgrade-" + UpgradeFiles[i] + ".png");
                item.Icon.raycastTarget = false;
                item.Number = CreateHeaderText(item.Root, size * .69f, "0", foreground);
                headerUpgradeItems[i] = item;
                item.Root.gameObject.SetActive(false);
            }
            RefreshHeader(state.StartedAt);
        }

        // Returns the monster-grid Y origin, including the gap below the entire header.
        private float RefreshHeader(double now)
        {
            float gridWidth = options.ShowMonsters
                ? Math.Max(0, (horizontal ? stripColumns : columns) * (options.MonsterSize + 10) - 10) : 0;
            // A header without monsters keeps its natural size instead of fitting to a zero-width grid.
            float availableWidth = horizontal || gridWidth <= 0 ? float.PositiveInfinity : gridWidth;
            RefreshHeaderLevel(now);
            var pills = new List<HeaderItem>();
            if (headerLevel.Root.gameObject.activeSelf)
            {
                FitHeaderItem(headerLevel, availableWidth);
                pills.Add(headerLevel);
            }
            RefreshHeaderCurrency(headerMap, state.MapValue, options.ShowMapValue,
                options.Translate("КАРТА", "MAP"), availableWidth, pills);
            RefreshHeaderCurrency(headerLoss, state.LostValue, options.ShowLostValue,
                options.Translate("ПОТЕРЯ", "LOSS"), availableWidth, pills);
            Vector2 summarySize = LayoutHeaderItems(headerSummaryPanel, pills, availableWidth, HeaderPillGap, false);

            var upgrades = new List<HeaderItem>();
            float size = Mathf.Clamp(options.UpgradeSize, 20, 64);
            foreach (HeaderItem item in headerUpgradeItems)
            {
                string key = UpgradeKeys[item.Order];
                state.Upgrades.TryGetValue(key, out int count);
                bool visible = options.ShowUpgrades && count > 0
                    && !(options.HiddenUpgrades?.Contains(key) ?? false);
                item.Root.gameObject.SetActive(visible);
                if (!visible) { item.Positioned = false; continue; }
                item.Count = count;
                string value = count.ToString(CultureInfo.InvariantCulture);
                SetHeaderFont(item.Number, teko, size * .69f);
                item.Number.Value = value;
                float iconWidth = size * .82f, iconHeight = size;
                if (item.Icon.texture != null)
                {
                    float fit = Math.Min(iconWidth / Math.Max(1, item.Icon.texture.width),
                        size / Math.Max(1, item.Icon.texture.height));
                    iconWidth = item.Icon.texture.width * fit;
                    iconHeight = item.Icon.texture.height * fit;
                }
                SetHeaderRect(item.Icon.rectTransform, 0, (size - iconHeight) / 2, iconWidth, iconHeight);
                float numberWidth = item.Number.Measure(value);
                float numberX = size * .82f + 3;
                SetHeaderRect(item.Number.rectTransform, numberX, 0, numberWidth, size);
                item.Root.sizeDelta = new Vector2(numberX + numberWidth + HeaderUpgradeGap, size);
                FitHeaderItem(item, availableWidth);
                upgrades.Add(item);
            }
            // An explicit definition-order tie break makes List.Sort deterministic.
            upgrades.Sort((left, right) =>
            {
                int byCount = right.Count.CompareTo(left.Count);
                return byCount != 0 ? byCount : left.Order.CompareTo(right.Order);
            });
            Vector2 upgradeSize = LayoutHeaderItems(headerUpgradePanel, upgrades, availableWidth,
                HeaderUpgradeGap, options.UpgradeRows == 2);
            bool hasSummary = pills.Count > 0;
            bool hasUpgrades = upgrades.Count > 0;
            headerSummaryPanel.gameObject.SetActive(hasSummary);
            headerUpgradePanel.gameObject.SetActive(hasUpgrades);

            float width, height;
            if (horizontal)
            {
                float gap = hasSummary && hasUpgrades ? HeaderPillGap : 0;
                width = summarySize.x + gap + upgradeSize.x;
                height = Math.Max(summarySize.y, upgradeSize.y);
                headerSummaryPanel.anchoredPosition = new Vector2(0, -(height - summarySize.y) / 2);
                headerUpgradePanel.anchoredPosition = new Vector2(summarySize.x + gap, -(height - upgradeSize.y) / 2);
            }
            else
            {
                float gap = hasSummary && hasUpgrades ? 6 : 0;
                width = Math.Max(summarySize.x, upgradeSize.x);
                height = summarySize.y + gap + upgradeSize.y;
                headerSummaryPanel.anchoredPosition = new Vector2(HeaderAlignmentOffset(width, summarySize.x), 0);
                headerUpgradePanel.anchoredPosition = new Vector2(HeaderAlignmentOffset(width, upgradeSize.x), -summarySize.y - gap);
            }
            layoutWidth = Math.Max(gridWidth, width) + 20;
            SetHeaderRect(headerPanel, 10 + HeaderAlignmentOffset(layoutWidth - 20, width), 0, width, height);
            headerPanel.gameObject.SetActive(hasSummary || hasUpgrades);
            return height > 0 ? height + 10 : 0;
        }

        private void RefreshHeaderLevel(double now)
        {
            bool visible = options.ShowLevel || options.ShowTimer;
            headerLevel.Root.gameObject.SetActive(visible);
            headerLevelCaption.gameObject.SetActive(options.ShowLevel);
            headerLevelNumber.gameObject.SetActive(options.ShowLevel);
            headerTimer.gameObject.SetActive(options.ShowTimer);
            headerTimerSeparator.gameObject.SetActive(options.ShowLevel && options.ShowTimer);
            string caption = options.Translate("УРОВЕНЬ", "LEVEL");
            SetHeaderCaption(headerLevelCaption, caption, 28, 20);
            if (!visible) { headerLevel.Positioned = false; return; }
            double elapsed = now - state.StartedAt;
            if (double.IsNaN(elapsed) || double.IsInfinity(elapsed)) elapsed = 0;
            long seconds = (long)Math.Min(int.MaxValue, Math.Max(0, Math.Floor(elapsed)));
            bool hours = seconds >= 3600;
            string timer = (hours ? (seconds / 3600).ToString("00", CultureInfo.InvariantCulture) + ":" : "")
                + (seconds / 60 % 60).ToString("00", CultureInfo.InvariantCulture) + ":"
                + (seconds % 60).ToString("00", CultureInfo.InvariantCulture);
            string level = state.Level.ToString(CultureInfo.InvariantCulture);
            headerLevelNumber.Value = level;
            headerTimer.Value = timer;
            float x = 13;
            if (options.ShowLevel)
            {
                float captionWidth = headerLevelCaption.Measure(caption);
                float numberWidth = headerLevelNumber.Measure(level);
                SetHeaderRect(headerLevelCaption.rectTransform, x, 4, captionWidth, 46);
                x += captionWidth + 8;
                SetHeaderRect(headerLevelNumber.rectTransform, x, 4, numberWidth, 46);
                x += numberWidth;
            }
            if (options.ShowTimer)
            {
                if (options.ShowLevel)
                {
                    x += 12;
                    SetHeaderRect(headerTimerSeparator, x, 4, 1, 46);
                    x += 13;
                }
                float timerWidth = Math.Max(hours ? 83 : 55, headerTimer.Measure(timer));
                SetHeaderRect(headerTimer.rectTransform, x, 4, timerWidth, 46);
                x += timerWidth;
            }
            headerLevel.Root.sizeDelta = new Vector2(x + 13, 53);
        }

        private HeaderItem CreateHeaderCurrency(string name, string caption, Color background, Color foreground)
        {
            var item = CreateHeaderPill(name, headerSummaryPanel, 63, background, 6);
            item.Caption = CreateHeaderText(item.Root, 22, caption, HeaderColor(247, 251, 255, .72f));
            item.Number = CreateHeaderText(item.Root, 34, "$0", foreground);
            return item;
        }

        private void RefreshHeaderCurrency(HeaderItem item, int amount, bool enabled, string caption,
            float availableWidth, List<HeaderItem> pills)
        {
            bool visible = enabled && amount > 0;
            item.Root.gameObject.SetActive(visible);
            SetHeaderCaption(item.Caption, caption, 22, 14);
            if (!visible) { item.Positioned = false; return; }
            string value = "$" + amount.ToString("N0", CultureInfo.InvariantCulture);
            item.Number.Value = value;
            float captionWidth = item.Caption.Measure(caption);
            float numberWidth = item.Number.Measure(value);
            if (options.WrapValueLabels)
            {
                float width = Math.Min(190, Math.Max(captionWidth, numberWidth) + 22);
                item.Root.sizeDelta = new Vector2(width, 63);
                SetHeaderRect(item.Caption.rectTransform, 11, 4, width - 22, 22);
                SetHeaderRect(item.Number.rectTransform, 11, 26, width - 22, 34);
            }
            else
            {
                item.Root.sizeDelta = new Vector2(captionWidth + 8 + numberWidth + 22, 53);
                SetHeaderRect(item.Caption.rectTransform, 11, 4, captionWidth, 46);
                SetHeaderRect(item.Number.rectTransform, 11 + captionWidth + 8, 4, numberWidth, 46);
            }
            FitHeaderItem(item, availableWidth);
            pills.Add(item);
        }

        private void SetHeaderCaption(NativeBitmapText label, string value, float englishSize, float russianSize)
        {
            bool english = options.Language == "en";
            SetHeaderFont(label, english ? teko : robotoBold, english ? englishSize : russianSize);
            label.Value = value;
        }

        private static void SetHeaderFont(NativeBitmapText label, NativeFontAtlas atlas, float size)
        {
            if (label.Atlas == atlas && label.Size == size) return;
            label.Atlas = atlas;
            label.Size = size;
            // Atlas and Size are fields, so neither invalidates the UI mesh/material automatically.
            label.SetVerticesDirty();
            label.SetMaterialDirty();
        }

        private float HeaderAlignmentOffset(float width, float contentWidth)
        {
            switch (options.Alignment)
            {
                case "Left": return 0;
                case "Right": return width - contentWidth;
                default: return (width - contentWidth) / 2;
            }
        }

        private NativeBitmapText CreateHeaderText(Transform parent, float size, string value, Color color)
        {
            var label = Text(parent, teko, size, 0, 0, 0, size);
            label.Centered = true;
            label.Value = value;
            label.color = color;
            label.raycastTarget = false;
            return label;
        }

        private static HeaderItem CreateHeaderPill(string name, Transform parent, float height, Color color, float radius)
        {
            var rect = Rect(name, parent, 0, 0, 0, height);
            var background = rect.gameObject.AddComponent<NativeRoundedPanel>();
            background.Radius = radius;
            background.color = color;
            background.raycastTarget = false;
            return new HeaderItem { Root = rect };
        }

        private static void FitHeaderItem(HeaderItem item, float availableWidth)
        {
            // A single large value (or a one-column grid) must not overflow even
            // when wrapping cannot help. Never apply requestedScale twice here.
            float scale = Math.Min(1, availableWidth / Math.Max(1, item.Root.sizeDelta.x));
            item.Root.localScale = new Vector3(scale, scale, 1);
        }

        private Vector2 LayoutHeaderItems(RectTransform parent, List<HeaderItem> items,
            float availableWidth, float gap, bool balanced)
        {
            var rows = new List<HeaderRow>();
            HeaderRow row = null;
            int split = (items.Count + 1) / 2;
            for (int i = 0; i < items.Count; i++)
            {
                HeaderItem item = items[i];
                // Preserve the web's ceil(n/2) split; narrow vertical layouts can
                // wrap either half into additional rows without changing order.
                if (row == null || (balanced && i == split)
                    || row.Width + gap + item.Width > availableWidth)
                {
                    row = new HeaderRow();
                    rows.Add(row);
                }
                if (row.Items.Count > 0) row.Width += gap;
                row.Items.Add(item);
                row.Width += item.Width;
                row.Height = Math.Max(row.Height, item.Height);
            }
            float width = 0, height = 0;
            foreach (HeaderRow line in rows)
            {
                width = Math.Max(width, line.Width);
                height += line.Height;
            }
            height += Math.Max(0, rows.Count - 1) * gap;
            float y = 0;
            foreach (HeaderRow line in rows)
            {
                float x = HeaderAlignmentOffset(width, line.Width);
                foreach (HeaderItem item in line.Items)
                {
                    item.TargetPosition = new Vector2(x, -y - (line.Height - item.Height) / 2);
                    if (!item.Positioned || !options.Animations) item.Root.anchoredPosition = item.TargetPosition;
                    item.Positioned = true;
                    x += item.Width + gap;
                }
                y += line.Height + gap;
            }
            var size = new Vector2(width, height);
            parent.sizeDelta = size;
            return size;
        }

        private void AnimateHeader(float dt)
        {
            float blend = options.Animations ? 1 - Mathf.Exp(-dt * 18) : 1;
            AnimateHeaderItem(headerLevel, blend);
            AnimateHeaderItem(headerMap, blend);
            AnimateHeaderItem(headerLoss, blend);
            foreach (var item in headerUpgradeItems) AnimateHeaderItem(item, blend);
        }

        private static void AnimateHeaderItem(HeaderItem item, float blend)
        {
            if (item.Root.gameObject.activeSelf)
                item.Root.anchoredPosition = Vector2.Lerp(item.Root.anchoredPosition, item.TargetPosition, blend);
        }

        private static void SetHeaderRect(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }

        private static Color HeaderColor(int red, int green, int blue, float alpha = 1) =>
            new Color(red / 255f, green / 255f, blue / 255f, alpha);

        private static void HeaderUpgradeColors(string key, out Color foreground, out Color background)
        {
            switch (key)
            {
                case "strength": case "range":
                    foreground = HeaderColor(255, 154, 61);
                    background = HeaderColor(255, 106, 22, .2f);
                    break;
                case "sprintSpeed": case "crouchRest": case "stamina":
                    foreground = HeaderColor(255, 224, 90);
                    background = HeaderColor(229, 179, 16, .2f);
                    break;
                case "health":
                    foreground = HeaderColor(140, 219, 46);
                    background = HeaderColor(140, 219, 46, .2f);
                    break;
                case "mapPlayerCount":
                    foreground = HeaderColor(217, 192, 138);
                    background = HeaderColor(203, 177, 140, .22f);
                    break;
                case "deathHeadBattery":
                    foreground = HeaderColor(224, 184, 120);
                    background = HeaderColor(203, 158, 90, .22f);
                    break;
                default:
                    foreground = HeaderColor(88, 189, 255);
                    background = HeaderColor(32, 151, 235, .2f);
                    break;
            }
        }
    }
}
