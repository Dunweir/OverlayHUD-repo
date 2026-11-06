using UnityEngine;
using UnityEngine.UI;

namespace OverlayHUD
{
    internal sealed partial class NativeHud
    {
        private readonly NativeAlertLifetime alertLifetime = new NativeAlertLifetime();
        private RectTransform alertPanel;
        private CanvasGroup alertGroup;
        private NativeBitmapText alertPlayer, alertAction, alertItem;

        private void CreateAlerts()
        {
            alertPanel = Fill("DestroyedItemAlert", panel, 0, 0, 0, NativeAlertLayout.Height,
                HeaderColor(225, 70, 70, .17f), NativeAlertLayout.Radius).rectTransform;
            // Clip glyph overhangs as well as text in exceptionally narrow viewports.
            alertPanel.gameObject.AddComponent<RectMask2D>();
            alertGroup = alertPanel.gameObject.AddComponent<CanvasGroup>();
            alertGroup.blocksRaycasts = false;
            alertGroup.interactable = false;
            var burst = Image("Burst", alertPanel, NativeAlertLayout.PaddingX, NativeAlertLayout.PaddingTop,
                NativeAlertLayout.FontSize, NativeAlertLayout.FontSize);
            burst.texture = Texture("destruction-emoji.png");
            alertPlayer = CreateAlertText(HeaderColor(255, 154, 154));
            alertAction = CreateAlertText(HeaderColor(247, 251, 255, .72f));
            alertItem = CreateAlertText(HeaderColor(255, 154, 154));
            alertPanel.gameObject.SetActive(false);
        }

        private void UpdateAlerts(double now)
        {
            alertLifetime.Update(state.LastDestroyedItem, options.ShowDestroyedItemAlerts, now);
            bool visible = alertLifetime.Current != null;
            alertPanel.gameObject.SetActive(visible);
            if (!visible) return;
            alertGroup.alpha = NativeAlertLayout.Opacity(now - alertLifetime.ShownAt, options.Animations);
        }

        private void PositionAlerts()
        {
            if (alertLifetime.Current == null) return;
            alertPanel.SetAsLastSibling();
            // FitToScreen uses height matching; all positions here are canvas units.
            float viewportHeight = scaler.referenceResolution.y;
            float viewportWidth = viewportHeight * HudViewportAspect;
            var current = alertLifetime.Current;
            var line = NativeAlertLayout.BuildLine(current.Player, options.Translate("разрушил", "destroyed"), current.Item,
                NativeAlertLayout.AvailableWidth(viewportWidth), alertPlayer.Measure);
            float panelLeft = viewportWidth * panel.anchorMin.x + panel.anchoredPosition.x - panel.pivot.x * panel.rect.width;
            float panelTop = viewportHeight * (1 - panel.anchorMin.y) - panel.anchoredPosition.y
                - (1 - panel.pivot.y) * panel.rect.height;
            var placement = NativeAlertLayout.Place(line.Width, viewportWidth, viewportHeight,
                panelLeft, panelTop, panel.rect.width, panel.rect.height, alertGroup.alpha);
            alertPanel.anchoredPosition = new Vector2(placement.X - panelLeft, panelTop - placement.Y);
            alertPanel.sizeDelta = new Vector2(placement.Width, placement.Height);
            SetAlertText(alertPlayer, line.Player, line.PlayerX, line.PlayerWidth);
            SetAlertText(alertAction, line.Action, line.ActionX, line.ActionWidth);
            SetAlertText(alertItem, line.Item, line.ItemX, line.ItemWidth);
        }

        private NativeBitmapText CreateAlertText(Color color)
        {
            var label = Text(alertPanel, teko, NativeAlertLayout.FontSize, 0, NativeAlertLayout.PaddingTop,
                0, NativeAlertLayout.FontSize);
            label.color = color;
            return label;
        }

        private static void SetAlertText(NativeBitmapText label, string value, float x, float width)
        {
            label.rectTransform.anchoredPosition = new Vector2(x, -NativeAlertLayout.PaddingTop);
            label.rectTransform.sizeDelta = new Vector2(width, NativeAlertLayout.FontSize);
            label.Value = value;
        }
    }

}
