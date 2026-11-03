using System;

namespace OverlayHUD
{
    // Pure layout math, shared by runtime and regression tests. Units are HUD pixels.
    internal static class NativeHudLayout
    {
        internal const float MonsterSize = 70;
        internal const float ColumnPitch = 80;
        internal const float RowPitch = 102;
        internal const float BorderWidth = 3.28125f;

        internal static float ContainScale(float width, float height, float boxSize) =>
            Math.Min(boxSize / Math.Max(1, width), boxSize / Math.Max(1, height));

        internal static float TextBaseline(float centerY, float lineTop, float lineBottom, float scale) =>
            centerY + (lineTop + lineBottom) * scale / 2;

        internal static float GridWidth(int cols) => Math.Max(0, cols * ColumnPitch - 10);

        internal static float Width(bool horizontal, int columns) => GridWidth(columns) + 20;

        internal static float MonsterRowPitch(float size, bool healthVisible) =>
            size * (healthVisible ? RowPitch : MonsterSize + 14) / MonsterSize;

        internal static float Ease(float t, float x1, float y1, float x2, float y2)
        {
            if (t <= 0) return 0;
            if (t >= 1) return 1;

            float low = 0, high = 1;
            for (int i = 0; i < 12; i++)
            {
                float u = (low + high) / 2;
                float v = 1 - u;
                float x = 3 * v * v * u * x1 + 3 * v * u * u * x2 + u * u * u;
                if (x < t) low = u;
                else high = u;
            }

            float parameter = (low + high) / 2;
            float inverse = 1 - parameter;
            return 3 * inverse * inverse * parameter * y1
                + 3 * inverse * parameter * parameter * y2 + parameter * parameter * parameter;
        }

        internal static int HealthBand(float ratio) =>
            ratio <= .1f ? 0 : ratio <= .3f ? 1 : ratio <= .6f ? 2 : 3;

        internal static float CooldownFraction(double now, double deadline, double duration)
        {
            if (duration <= 0 || double.IsNaN(duration) || double.IsInfinity(duration)
                || double.IsNaN(now) || double.IsInfinity(now)
                || double.IsNaN(deadline) || double.IsInfinity(deadline)) return 0;

            double remaining = deadline - now;
            if (double.IsNaN(remaining) || double.IsInfinity(remaining)) return 0;
            return (float)Math.Max(0, Math.Min(1, remaining / duration));
        }

        // CanvasScaler matches height. Correct its requested scale so the physical
        // baseline matches the web's min(width / 1920, height / 1080), not height alone.
        internal static float ViewportScale(float requested, int screenWidth, int screenHeight) =>
            requested * Math.Min(1, 1080f * Math.Max(1, screenWidth) / (1920f * Math.Max(1, screenHeight)));

        internal static float FitScale(float requested, int screenWidth, int screenHeight, float width, float height)
        {
            float availableWidth = 1080f * Math.Max(1, screenWidth) / Math.Max(1, screenHeight);
            return Math.Max(.01f, Math.Min(requested,
                Math.Min(availableWidth / (width + 40), 1080f / (height + 40))));
        }

        internal static float ClampX(float offset, float viewportWidth, float panelWidth)
        {
            float limit = Math.Max(0, (viewportWidth - panelWidth) / 2 - 20);
            return Math.Max(-limit, Math.Min(offset, limit));
        }

        internal static float ClampY(float offset, float viewportHeight, float panelHeight) =>
            Math.Max(20, Math.Min(offset, viewportHeight - panelHeight - 20));
    }
}
