using System;
using UnityEngine;
using UnityEngine.UI;

namespace OverlayHUD
{
    internal sealed partial class NativeHud
    {
        private const float AlertHeight = 64, AlertGap = 14;
        private readonly NativeAlertLifetime alertLifetime = new NativeAlertLifetime();
        private RectTransform alertPanel;
        private CanvasGroup alertGroup;
        private NativeBitmapText alertPlayer, alertItem;

        private void CreateAlerts()
        {
            alertPanel = Fill("DestroyedItemAlert", panel, 0, 0, 0, AlertHeight,
                new Color32(45, 18, 20, 244), 9).rectTransform;
            // Clip glyph overhangs as well as text in exceptionally narrow viewports.
            alertPanel.gameObject.AddComponent<RectMask2D>();
            alertGroup = alertPanel.gameObject.AddComponent<CanvasGroup>();
            alertGroup.blocksRaycasts = false;
            alertGroup.interactable = false;
            var burst = Rect("Burst", alertPanel, 12, 16, 32, 32).gameObject.AddComponent<NativeAlertBurst>();
            burst.color = new Color32(255, 157, 91, 255);
            burst.raycastTarget = false;
            alertPlayer = Text(alertPanel, robotoBold, 22, 56, 7, 0, 25);
            alertPlayer.color = new Color32(255, 192, 182, 255);
            alertItem = Text(alertPanel, robotoBold, 20, 56, 33, 0, 24);
            alertItem.color = new Color32(255, 241, 235, 255);
            alertPanel.gameObject.SetActive(false);
        }

        private void UpdateAlerts(double now)
        {
            alertLifetime.Update(state.LastDestroyedItem, options.ShowDestroyedItemAlerts, now);
            bool visible = alertLifetime.Current != null;
            alertPanel.gameObject.SetActive(visible);
            if (!visible) return;
            double elapsed = now - alertLifetime.ShownAt;
            alertGroup.alpha = options.Animations
                ? Mathf.Clamp01((float)Math.Min(elapsed / .18, (NativeAlertLifetime.ShowDuration - elapsed) / .3))
                : 1;
        }

        private void PositionAlerts()
        {
            if (alertLifetime.Current == null) return;
            alertPanel.SetAsLastSibling();
            // FitToScreen uses height matching; all positions here are canvas units.
            float viewportHeight = scaler.referenceResolution.y;
            float viewportWidth = viewportHeight * HudViewportAspect;
            const float margin = 2;
            float width = Mathf.Max(0, Mathf.Min(640, Mathf.Min(Mathf.Max(360, layoutWidth), viewportWidth - margin * 2)));
            float height = Mathf.Max(0, Mathf.Min(AlertHeight, viewportHeight - margin * 2));
            float panelLeft = viewportWidth * panel.anchorMin.x + panel.anchoredPosition.x - panel.pivot.x * panel.rect.width;
            float panelTop = viewportHeight * (1 - panel.anchorMin.y) - panel.anchoredPosition.y
                - (1 - panel.pivot.y) * panel.rect.height;
            float x = Mathf.Clamp(panelLeft + (panel.rect.width - width) / 2, margin,
                Mathf.Max(margin, viewportWidth - width - margin));
            float y = panelTop - height - AlertGap;
            if (y < margin) y = panelTop + panel.rect.height + AlertGap;
            // If neither side fits, keeping the alert on-screen takes precedence over the gap.
            y = Mathf.Clamp(y, margin, Mathf.Max(margin, viewportHeight - height - margin));
            alertPanel.anchoredPosition = new Vector2(x - panelLeft, panelTop - y);
            alertPanel.sizeDelta = new Vector2(width, height);
            float textWidth = Mathf.Max(0, width - 68);
            alertPlayer.rectTransform.sizeDelta = new Vector2(textWidth, 25);
            alertItem.rectTransform.sizeDelta = new Vector2(textWidth, 24);
            var current = alertLifetime.Current;
            alertPlayer.Value = FitAlertText(alertPlayer, current.Player + options.Translate(" разрушил", " destroyed"), textWidth);
            alertItem.Value = FitAlertText(alertItem, current.Item, textWidth);
        }

        private static string FitAlertText(NativeBitmapText label, string value, float width)
        {
            if (label.Measure(value) <= width) return value;
            const string suffix = "...";
            if (label.Measure(suffix) > width) return "";
            int length = value.Length;
            while (length > 0)
            {
                length--;
                if (length > 0 && char.IsHighSurrogate(value[length - 1])) length--;
                string shortened = value.Substring(0, length).TrimEnd() + suffix;
                if (label.Measure(shortened) <= width) return shortened;
            }
            return suffix;
        }
    }

    // Embedded atlases have no explosion emoji; draw the burst without a font dependency.
    public sealed class NativeAlertBurst : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            var bounds = rectTransform.rect;
            float radius = Mathf.Min(bounds.width, bounds.height) / 2;
            if (radius <= 0) return;
            mesh.AddVert(bounds.center, color, Vector2.zero);
            const int points = 16;
            for (int i = 0; i < points; i++)
            {
                float angle = (90 + i * 360f / points) * Mathf.Deg2Rad;
                float r = radius * (i % 2 == 0 ? 1 : .45f);
                mesh.AddVert(bounds.center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * r, color, Vector2.zero);
            }
            for (int i = 0; i < points; i++) mesh.AddTriangle(0, i + 1, (i + 1) % points + 1);
        }
    }
}
