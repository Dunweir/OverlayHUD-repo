using System.Globalization;
using Newtonsoft.Json.Linq;
using OverlayHUD;

internal static class AlertLayoutTests
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void Near(float actual, float expected, string message) =>
        Check(Math.Abs(actual - expected) < .001f, message);

    internal static void Run()
    {
        // Use the same advances/fallback as NativeBitmapText, not guessed character widths.
        using var stream = typeof(AlertLayoutTests).Assembly.GetManifestResourceStream("AlertTekoAtlas.json");
        using var reader = new StreamReader(stream!);
        var atlas = JObject.Parse(reader.ReadToEnd());
        var advances = atlas["glyphs"]!.ToDictionary(g => (int)g["code"]!, g => (float)g["advance"]!);
        float Measure(string text) => text.Sum(c => advances.GetValueOrDefault(c, advances['?']))
            * NativeAlertLayout.FontSize / (float)atlas["size"]!;

        Check(NativeAlertLayout.FontSize == 26 && NativeAlertLayout.Height == 37
            && NativeAlertLayout.PaddingX == 18 && NativeAlertLayout.PaddingTop == 6
            && NativeAlertLayout.Gap == 8 && NativeAlertLayout.Radius == 7 && NativeAlertLayout.HudGap == 14,
            "Destroyed-item pill dimensions drifted from Electron CSS");
        var shortLine = NativeAlertLayout.BuildLine("Bob", "destroyed", "Cup", 1900, Measure);
        Check(shortLine.Player == "BOB" && shortLine.Action == "DESTROYED" && shortLine.Item == "CUP",
            "English alert is not uppercase or was truncated despite fitting");
        Near(shortLine.Width, 36 + 26 + Measure(" ") + 16 + Measure("BOBDESTROYEDCUP"),
            "Alert width is not max-content plus padding and flex gaps");
        Check(shortLine.Width < 360, "Short alerts retained the old minimum width");
        Near(shortLine.ActionX - shortLine.PlayerX - shortLine.PlayerWidth, 8, "Player/action flex gap changed");
        Near(shortLine.ItemX - shortLine.ActionX - shortLine.ActionWidth, 8, "Action/item flex gap changed");
        Near(shortLine.Width - shortLine.ItemX - shortLine.ItemWidth, 18, "Right padding changed");
        var longLine = NativeAlertLayout.BuildLine(new string('W', 60), "destroyed", new string('W', 80), 1900, Measure);
        Check(longLine.Width > 640, "Long alerts retained the old arbitrary width cap");

        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            var ru = NativeAlertLayout.BuildLine("Иван Smith", "разрушил", "Золотая ваза", 1900, Measure);
            Check(ru.Player == "ИВАН SMITH" && ru.Action == "РАЗРУШИЛ" && ru.Item == "ЗОЛОТАЯ ВАЗА",
                "Alert capitalization depends on system culture or loses Cyrillic");
        }
        finally { CultureInfo.CurrentCulture = culture; }

        foreach (float width in new[] { 0f, 1, 20, 60, 100, 200, 320, 640, 1920 })
        {
            var line = NativeAlertLayout.BuildLine(new string('Я', 60), "разрушил", new string('W', 80), width, Measure);
            Check(line.Width >= 0 && line.Width <= width, "Alert line exceeds available width");
            foreach (var part in new[] { (line.Player, line.PlayerX, line.PlayerWidth),
                (line.Action, line.ActionX, line.ActionWidth), (line.Item, line.ItemX, line.ItemWidth) })
            {
                Near(part.Item3, Measure(part.Item1), "Fitted label would shrink its font");
                Check(part.Item1.Length == 0 || part.Item2 + part.Item3 <= line.Width - 18 + .001f,
                    "Nonempty alert label exceeds its content bounds");
            }
        }
        var cramped = NativeAlertLayout.BuildLine(new string('W', 60), "destroyed", new string('W', 80), 320, Measure);
        Check(cramped.Action == "DESTROYED" && cramped.Player.EndsWith("...") && cramped.Item.EndsWith("..."),
            "Narrow alert lost the action or failed to ellipsize both names");
        Check(NativeAlertLayout.FitText("AB😀CDE", 6, s => s.Length) == "AB...",
            "Ellipsis split a surrogate pair");
        Check(NativeAlertLayout.FitText("ABCDE", 2, s => s.Length) == "", "Ellipsis overflowed tiny space");
        Check(NativeAlertLayout.FitText("ABCDE", 5, s => s.Length) == "ABCDE", "Exact fit was truncated");
        Check(NativeAlertLayout.FitText("ABCDE", 3, s => s.Length) == "...", "Ellipsis-only fit was lost");

        var above = NativeAlertLayout.Place(300, 1920, 1080, 500, 200, 600, 200, 1);
        Check(!above.Below, "Alert did not prefer available space above HUD");
        Near(above.X, 650, "Alert is not centered on HUD");
        Near(above.Y, 149, "Above-HUD gap changed");
        var below = NativeAlertLayout.Place(300, 1920, 1080, 500, 20, 600, 200, 1);
        Check(below.Below, "Top-edge HUD did not place alert below");
        Near(below.Y, 234, "Below-HUD gap changed");
        Near(NativeAlertLayout.Place(300, 1920, 1080, 500, 200, 600, 200, 0).Y, above.Y + 6,
            "Above alert does not slide away from HUD");
        Near(NativeAlertLayout.Place(300, 1920, 1080, 500, 20, 600, 200, 0).Y, below.Y - 6,
            "Below alert slide direction is wrong");
        Check(!NativeAlertLayout.Place(300, 800, 150, 100, 40, 600, 90, 1).Below,
            "Neither side fits: failed to choose the larger upper space");
        Check(NativeAlertLayout.Place(300, 800, 150, 100, 20, 600, 90, 1).Below,
            "Neither side fits: failed to choose the larger lower space");

        // Include extreme aspect ratios, requested scales and all edge alignments.
        foreach (var viewport in new[] { (1920, 1080), (3440, 1440), (800, 1200), (320, 200) })
        foreach (float requested in new[] { .5f, 1, 2 })
        foreach (float alignment in new[] { 0f, .5f, 1 })
        foreach (float vertical in new[] { 0f, .5f, 1 })
        foreach (float opacity in new[] { 0f, .5f, 1 })
        {
            float scale = NativeHudLayout.FitScale(
                NativeHudLayout.ViewportScale(requested, viewport.Item1, viewport.Item2),
                viewport.Item1, viewport.Item2, 600, 400);
            float vh = 1080 / scale, vw = vh * viewport.Item1 / viewport.Item2;
            var line = NativeAlertLayout.BuildLine(new string('W', 60), "destroyed", new string('Я', 80),
                NativeAlertLayout.AvailableWidth(vw), Measure);
            var position = NativeAlertLayout.Place(line.Width, vw, vh,
                (vw - 600) * alignment, (vh - 400) * vertical, 600, 400, opacity);
            Check(position.X >= 2 && position.Y >= 2 && position.X + position.Width <= vw - 2 + .001f
                && position.Y + position.Height <= vh - 2 + .001f, "Animated/scaled alert escaped viewport");
        }
        foreach (float size in new[] { 0f, 1, 3, 4, 10, 30 })
        {
            var tiny = NativeAlertLayout.Place(800, size, size, -100, -100, 600, 400, 0);
            Check(tiny.X >= 0 && tiny.Y >= 0 && tiny.Width >= 0 && tiny.Height >= 0
                && tiny.X + tiny.Width <= size && tiny.Y + tiny.Height <= size, "Tiny viewport clamp failed");
        }

        Near(NativeAlertLayout.Opacity(0, true), 0, "Alert did not start transparent");
        Near(NativeAlertLayout.Opacity(.25, true), 1, "Alert entrance is not 250 ms");
        Near(NativeAlertLayout.Opacity(5.75, true), 1, "Alert exit began early");
        Near(NativeAlertLayout.Opacity(6, true), 0, "Alert fade extended its lifetime");
        Near(NativeAlertLayout.Opacity(.125, true), NativeHudLayout.Ease(.5f, .25f, .1f, .25f, 1),
            "Alert transition does not use CSS ease");
        for (int i = 0; i <= 100; i++)
        {
            double elapsed = i * .06;
            float opacity = NativeAlertLayout.Opacity(elapsed, true);
            Check(opacity >= 0 && opacity <= 1, "Alert animation alpha is out of bounds");
            Near(NativeAlertLayout.Opacity(elapsed, false), 1, "Animations=false retained a fade/slide");
        }
        Console.WriteLine("Native HUD destroyed-item alert layout/animation checks passed.");
    }
}
