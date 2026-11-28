using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace OverlayHUD
{
    internal sealed partial class NativeHud
    {
        private readonly NativeAlertLifetime alertLifetime = new NativeAlertLifetime();
        private sealed class AlertRow
        {
            internal RectTransform Panel;
            internal CanvasGroup Group;
            internal NativeBitmapText Player, Action, Item;
            internal TextMeshProUGUI GamePlayer, GameAction, GameItem, FontSource;
            internal NativeRoundedPanel Backing;
            internal Image Scanlines;
            internal RawImage Burst, Reaction;
            internal Shadow BurstShadow, ReactionShadow;
            internal bool HasReaction;
            internal NativeHudState.DestroyedItemAlert LaidOut;
            internal float ViewportWidth;
            internal NativeAlertLayout.Line Line;
        }
        private readonly List<AlertRow> alertRows = new List<AlertRow>();
        private RectTransform alertLayer;
        private Vector2 alertAnchor;
        private bool alertBelow;
        private long lastPositionedSequence;
        private const float AlertLetterDelay = .075f;

        private AlertRow CreateAlertRow()
        {
            var row = new AlertRow();
            row.Backing = Fill("DestroyedItemAlert", alertLayer, 0, 0, 0, NativeAlertLayout.Height,
                HeaderColor(225, 70, 70, .17f), NativeAlertLayout.Radius);
            row.Panel = row.Backing.rectTransform;
            row.Scanlines = StretchStyleRect("Scanlines", row.Panel).gameObject.AddComponent<Image>();
            row.Scanlines.raycastTarget = false;
            row.Scanlines.enabled = false;
            // Clip glyph overhangs as well as text in exceptionally narrow viewports.
            row.Panel.gameObject.AddComponent<RectMask2D>();
            row.Group = row.Panel.gameObject.AddComponent<CanvasGroup>();
            row.Group.blocksRaycasts = false;
            row.Group.interactable = false;
            row.Burst = Image("Burst", row.Panel, NativeAlertLayout.PaddingX, NativeAlertLayout.PaddingTop,
                NativeAlertLayout.FontSize, NativeAlertLayout.FontSize);
            row.Burst.texture = Texture("destruction-emoji.png");
            row.Reaction = Image("TaxmanReaction", row.Panel, 0, 0, 32, 32);
            row.Reaction.enabled = false;
            row.Player = CreateAlertText(row.Panel, HeaderColor(255, 154, 154));
            row.Action = CreateAlertText(row.Panel, HeaderColor(247, 251, 255, .72f));
            row.Item = CreateAlertText(row.Panel, HeaderColor(255, 154, 154));
            row.BurstShadow = AddGameShadow(row.Burst);
            row.ReactionShadow = AddGameShadow(row.Reaction);
            return row;
        }

        private void UpdateAlerts(double now)
        {
            alertLifetime.Update(state.DestroyedItems, options.ShowDestroyedItemAlerts, now);
            alertLayer.gameObject.SetActive(alertLifetime.Active.Count > 0);
        }

        private void PositionAlerts(float now)
        {
            var active = alertLifetime.Active;
            if (active.Count == 0) return;
            if (alertLayer.GetSiblingIndex() != panel.childCount - 1) alertLayer.SetAsLastSibling();
            float viewportHeight = scaler.referenceResolution.y;
            float viewportWidth = viewportHeight * HudViewportAspect;
            float panelLeft = viewportWidth * panel.anchorMin.x + panel.anchoredPosition.x - panel.pivot.x * panel.rect.width;
            float panelTop = viewportHeight * (1 - panel.anchorMin.y) - panel.anchoredPosition.y
                - (1 - panel.pivot.y) * panel.rect.height;
            const float gap = NativeAlertLayout.Slide + 2;
            const float pitch = NativeAlertLayout.Height + gap;
            int capacity = Mathf.Max(1, Mathf.FloorToInt((viewportHeight + gap) / pitch));
            int count = Mathf.Min(active.Count, capacity);
            if (active[0].Event.Sequence > lastPositionedSequence)
            {
                var placement = NativeAlertLayout.Place(0, viewportWidth, viewportHeight,
                    panelLeft, panelTop, panel.rect.width, panel.rect.height, 1);
                alertAnchor = new Vector2(placement.X / viewportWidth, placement.Y / viewportHeight);
                alertBelow = placement.Below;
            }
            lastPositionedSequence = active[active.Count - 1].Event.Sequence;
            float height = Mathf.Min(NativeAlertLayout.Height, viewportHeight);
            float stackHeight = height + (count - 1) * pitch;
            float top = alertAnchor.y * viewportHeight - (alertBelow ? 0 : stackHeight - height);
            top = Mathf.Clamp(top, 0, viewportHeight - stackHeight);
            while (alertRows.Count < count) alertRows.Add(CreateAlertRow());
            for (int i = 0; i < alertRows.Count; i++)
            {
                var row = alertRows[i];
                row.Panel.gameObject.SetActive(i < count);
                if (i >= count) continue;
                var entry = active[active.Count - count + i];
                float age = (float)(now - entry.ShownAt);
                row.Group.alpha = NativeAlertLayout.Opacity(age, options.Animations);
                RenderAlert(row, entry.Event, age, viewportWidth);
                float width = row.Line.Width;
                bool gameMotion = options.GameStyle && options.Animations;
                float scale = gameMotion ? Mathf.Min(1 + GameSpring(age, .1f, 2.5f, .2f), viewportWidth / Mathf.Max(1, width)) : 1;
                row.Panel.localScale = Vector3.one * scale;
                float x = Mathf.Clamp(alertAnchor.x * viewportWidth - width * scale / 2, 0, viewportWidth - width * scale);
                float slide = (alertBelow ? -1 : 1) * NativeAlertLayout.Slide * (1 - row.Group.alpha);
                if (gameMotion)
                {
                    float exit = (age - (float)NativeAlertLifetime.ShowDuration + .25f) * 4;
                    slide = age < .3f ? -GameTextEnterY(age, height) : -GameTextExitY(exit, height);
                }
                float y = Mathf.Clamp(top + i * pitch + slide, 0, viewportHeight - height * scale);
                row.Panel.anchoredPosition = new Vector2(x - panelLeft, panelTop - y);
                row.Panel.sizeDelta = new Vector2(width, height);
            }
        }

        private void RenderAlert(AlertRow row, NativeHudState.DestroyedItemAlert current, float age, float viewportWidth)
        {
            var source = GameHeaderReady ? quotaSource : null;
            Rect reaction = current.Sequence % 2 == 0 ? sadUV : lossUV;
            bool hasReaction = options.GameStyle && current.Sequence % 3 == 0 && viewportWidth >= 240
                && taxmanEmojis != null && reaction.width > 0;
            if (current != row.LaidOut || viewportWidth != row.ViewportWidth || !ReferenceEquals(source, row.FontSource)
                || hasReaction != row.HasReaction)
            {
                row.HasReaction = hasReaction;
                row.LaidOut = current;
                row.ViewportWidth = viewportWidth;
                row.FontSource = source;
                row.Player.color = row.Item.color = options.GameStyle ? GameLossColor : HeaderColor(255, 154, 154);
                row.Action.color = options.GameStyle ? Color.white : HeaderColor(247, 251, 255, .72f);
                StyleText(row.Player, "", ref row.GamePlayer, teko, useScanlines: false, compactBold: true, alert: true);
                row.Line = NativeAlertLayout.BuildLine(current.Player, options.Translate("разрушил", "destroyed"), current.Item,
                    NativeAlertLayout.AvailableWidth(viewportWidth), value => MeasureAlertText(row, value), hasReaction ? 36 : 0);
                SetAlertText(row.Player, ref row.GamePlayer, row.Line.Player, row.Line.PlayerX, row.Line.PlayerWidth);
                SetAlertText(row.Action, ref row.GameAction, row.Line.Action, row.Line.ActionX, row.Line.ActionWidth);
                SetAlertText(row.Item, ref row.GameItem, row.Line.Item, row.Line.ItemX, row.Line.ItemWidth);
            }
            int length = row.Line.Player.Length + row.Line.Action.Length + row.Line.Item.Length + 3;
            // Keep long names readable within the existing six-second notification.
            float delay = Mathf.Min(AlertLetterDelay, 2.25f / length);
            int letters = options.GameStyle && options.Animations
                ? Mathf.Max(0, Mathf.FloorToInt(age / delay)) : int.MaxValue;
            RevealAlertText(row.Player, row.GamePlayer, row.Line.Player, letters);
            letters -= row.Line.Player.Length + 1;
            RevealAlertText(row.Action, row.GameAction, row.Line.Action, letters);
            letters -= row.Line.Action.Length + 1;
            RevealAlertText(row.Item, row.GameItem, row.Line.Item, letters);
            row.Reaction.enabled = hasReaction && letters > row.Line.Item.Length;
            if (row.Reaction.enabled)
            {
                row.Reaction.texture = taxmanEmojis.spriteSheet;
                row.Reaction.uvRect = reaction;
                SetHeaderRect(row.Reaction.rectTransform, row.Line.ItemX + row.Line.ItemWidth + 4,
                    NativeAlertLayout.PaddingTop - 3, 32, 32);
            }
            row.Backing.enabled = !options.GameStyle;
            ApplyScanlines(row.Scanlines, row.Item.color);
            bool playerIcon = options.GameStyle && taxmanEmojis != null && playerUV.width > 0;
            row.Burst.texture = playerIcon ? (Texture)EmojiTexture(playerUV) : Texture("destruction-emoji.png");
            row.Burst.enabled = playerIcon || !options.GameStyle;
            float burstSize = playerIcon ? 36 : NativeAlertLayout.FontSize;
            float inset = (NativeAlertLayout.FontSize - burstSize) * .5f;
            SetHeaderRect(row.Burst.rectTransform, NativeAlertLayout.PaddingX + inset,
                NativeAlertLayout.PaddingTop + inset, burstSize, burstSize);
            row.BurstShadow.enabled = row.ReactionShadow.enabled = options.GameStyle;
        }

        private static void RevealAlertText(NativeBitmapText bitmap, TextMeshProUGUI text, string value, int letters)
        {
            int visible = Mathf.Clamp(letters, 0, value.Length);
            bitmap.VisibleCharacters = visible;
            if (text != null) text.maxVisibleCharacters = visible;
        }

        private float MeasureAlertText(AlertRow row, string value)
        {
            if (!GameHeaderReady) return row.Player.Measure(value);
            return Mathf.Ceil(row.GamePlayer.GetPreferredValues(value).x) + 3;
        }

        private NativeBitmapText CreateAlertText(RectTransform parent, Color color)
        {
            var label = Text(parent, teko, NativeAlertLayout.FontSize, 0, NativeAlertLayout.PaddingTop,
                0, NativeAlertLayout.FontSize);
            label.color = color;
            return label;
        }

        private void SetAlertText(NativeBitmapText label, ref TextMeshProUGUI gameText, string value, float x, float width)
        {
            label.rectTransform.anchoredPosition = new Vector2(x, -NativeAlertLayout.PaddingTop);
            label.rectTransform.sizeDelta = new Vector2(width, NativeAlertLayout.FontSize);
            label.Value = value;
            StyleText(label, value, ref gameText, teko, useScanlines: false, compactBold: true, alert: true);
            if (label.enabled || gameText == null) return;
            // All three spans share a baseline, including glyphs in dynamic fallback atlases.
            gameText.alignment = TextAlignmentOptions.Baseline;
            gameText.rectTransform.anchoredPosition = new Vector2(0, -NativeAlertLayout.FontSize * .4f);
        }
    }

}
