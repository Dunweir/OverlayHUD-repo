using System;

namespace OverlayHUD
{
    // Electron's .item-destroyed-alert, in canvas units; screen safety takes
    // precedence over max-content/nowrap when the message cannot fit.
    internal static class NativeAlertLayout
    {
        internal const float FontSize = 26, PaddingX = 18, PaddingTop = 6;
        internal const float Height = FontSize + PaddingTop + 5, Radius = 7, Gap = 8;
        internal const float HudGap = 14, Slide = 6, Transition = .25f;
        private const float ScreenMargin = 2;

        internal struct Line
        {
            internal string Player, Action, Item;
            internal float Width, PlayerX, ActionX, ItemX;
            internal float PlayerWidth, ActionWidth, ItemWidth;
        }

        internal struct Placement
        {
            internal float X, Y, Width, Height;
            internal bool Below;
        }

        internal static float AvailableWidth(float viewportWidth) => Math.Max(0, viewportWidth - ScreenMargin * 2);

        internal static Line BuildLine(string player, string action, string item, float availableWidth, Func<string, float> measure)
        {
            player = player.ToUpperInvariant();
            action = action.ToUpperInvariant();
            item = item.ToUpperInvariant();
            float prefix = FontSize + measure(" ");
            float fixedWidth = PaddingX * 2 + prefix + Gap * 2;
            float textBudget = Math.Max(0, availableWidth - fixedWidth);
            // Preserve the action first; share remaining space between the two names.
            action = FitText(action, textBudget, measure);
            float actionWidth = measure(action);
            float namesBudget = Math.Max(0, textBudget - actionWidth);
            float playerBudget = Math.Min(measure(player), Math.Max(namesBudget / 2, namesBudget - measure(item)));
            player = FitText(player, playerBudget, measure);
            float playerWidth = measure(player);
            item = FitText(item, Math.Max(0, namesBudget - playerWidth), measure);
            float itemWidth = measure(item);
            return new Line
            {
                Player = player, Action = action, Item = item,
                Width = Math.Max(0, Math.Min(availableWidth, fixedWidth + playerWidth + actionWidth + itemWidth)),
                PlayerX = PaddingX + prefix,
                ActionX = PaddingX + prefix + playerWidth + Gap,
                ItemX = PaddingX + prefix + playerWidth + Gap + actionWidth + Gap,
                PlayerWidth = playerWidth, ActionWidth = actionWidth, ItemWidth = itemWidth
            };
        }

        internal static string FitText(string value, float width, Func<string, float> measure)
        {
            if (measure(value) <= width) return value;
            const string suffix = "...";
            if (measure(suffix) > width) return "";
            int length = value.Length;
            while (length > 0)
            {
                length--;
                if (length > 0 && char.IsHighSurrogate(value[length - 1])) length--;
                string shortened = value.Substring(0, length).TrimEnd() + suffix;
                if (measure(shortened) <= width) return shortened;
            }
            return suffix;
        }

        internal static Placement Place(float width, float viewportWidth, float viewportHeight,
            float panelLeft, float panelTop, float panelWidth, float panelHeight, float opacity)
        {
            float marginX = Math.Min(ScreenMargin, viewportWidth / 2);
            float marginY = Math.Min(ScreenMargin, viewportHeight / 2);
            width = Math.Max(0, Math.Min(width, viewportWidth - marginX * 2));
            float height = Math.Max(0, Math.Min(Height, viewportHeight - marginY * 2));
            float above = panelTop - marginY;
            float below = viewportHeight - marginY - panelTop - panelHeight;
            bool placeBelow = above < height + HudGap && (below >= height + HudGap || below > above);
            float y = placeBelow ? panelTop + panelHeight + HudGap : panelTop - height - HudGap;
            y += (placeBelow ? -Slide : Slide) * (1 - opacity);
            return new Placement
            {
                X = Clamp(panelLeft + (panelWidth - width) / 2, marginX, viewportWidth - width - marginX),
                Y = Clamp(y, marginY, viewportHeight - height - marginY),
                Width = width, Height = height, Below = placeBelow
            };
        }

        internal static float Opacity(double elapsed, bool animations)
        {
            if (!animations) return 1;
            // Keep the existing six-second lifetime, including the exit transition.
            float entering = NativeHudLayout.Ease((float)(elapsed / Transition), .25f, .1f, .25f, 1);
            float leaving = NativeHudLayout.Ease((float)((elapsed - NativeAlertLifetime.ShowDuration + Transition) / Transition),
                .25f, .1f, .25f, 1);
            return Math.Min(entering, 1 - leaving);
        }

        private static float Clamp(float value, float min, float max) => Math.Max(min, Math.Min(value, max));
    }
}
