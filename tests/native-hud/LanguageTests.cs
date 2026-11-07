using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using OverlayHUD;

internal static class LanguageTests
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    internal static void Run()
    {
        foreach (string tag in new[] { "ru", "ru-RU", "ru-BY", "ru-KZ", "RU-ru" })
            Check(NativeHudLanguage.FromCulture(CultureInfo.GetCultureInfo(tag)) == "ru",
                "Russian primary language was not recognized: " + tag);
        foreach (string tag in new[] { "en", "en-US", "en-GB", "uk-UA", "be-BY", "de-DE", "fr-FR", "ja-JP", "tr-TR" })
            Check(NativeHudLanguage.FromCulture(CultureInfo.GetCultureInfo(tag)) == "en",
                "Non-Russian UI language did not default to English: " + tag);
        Check(NativeHudLanguage.FromCulture(CultureInfo.InvariantCulture) == "en"
            && NativeHudLanguage.FromCulture(null) == "en", "Missing UI language must default to English");

        string expected;
        if (OperatingSystem.IsWindows())
        {
            // Independent oracle: LANGID's low ten bits are the primary language (LANG_RUSSIAN = 0x19).
            ushort languageId = GetUserDefaultUILanguage();
            expected = (languageId & 0x3ff) == 0x19 ? "ru" : "en";
            Console.WriteLine($"Windows user UI LANGID: 0x{languageId:x4}; expected HUD default: {expected}");
        }
        else
        {
            expected = NativeHudLanguage.FromCulture(CultureInfo.InstalledUICulture);
        }
        Check(NativeHudLanguage.DefaultLanguage == expected, "Cached default does not match the OS UI language");

        var current = CultureInfo.CurrentCulture;
        var currentUi = CultureInfo.CurrentUICulture;
        var defaultUi = CultureInfo.DefaultThreadCurrentUICulture;
        try
        {
            // Simulate the game/mod changing both thread and process defaults, in both directions.
            foreach (string tag in new[] { "ru-RU", "en-US", "tr-TR" })
            {
                var culture = CultureInfo.GetCultureInfo(tag);
                CultureInfo.CurrentCulture = culture;
                CultureInfo.CurrentUICulture = culture;
                CultureInfo.DefaultThreadCurrentUICulture = culture;
                Check(NativeHudLanguage.ReadSystemLanguage() == expected,
                    "OS detection followed mutable game/thread culture: " + tag);
                Check(new NativeHudOptions().Language == expected,
                    "NativeHudOptions disagrees with the config creation default: " + tag);
                foreach (string saved in new[] { "ru", "en" })
                {
                    var options = new NativeHudOptions { Language = saved };
                    Check(options.Language == saved && options.Translate("ru", "en") == saved,
                        "Explicit language was overridden by the system default: " + saved);
                }
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = current;
            CultureInfo.CurrentUICulture = currentUi;
            CultureInfo.DefaultThreadCurrentUICulture = defaultUi;
        }

        // Match the existing source-contract approach without loading Unity/BepInEx in this test runner.
        // Config.Bind applies its default only when the key is absent; existing ru/en must flow unchanged
        // into NativeHudOptions. Neither a migration nor an auto mode should replace saved choices.
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Plugin.Native.source"));
        Check(Regex.IsMatch(source,
            "nativeLanguage = BindNativeSetting\\(NativeGeneralSection, \"Language\", NativeHudLanguage.DefaultLanguage,\\s*\"[^\"]*\", 90, new AcceptableValueList<string>\\(\"ru\", \"en\"\\)\\);"),
            "Language binding must use the shared OS default and offer only ru/en");
        Check(source.Contains("return Config.Bind(section, key, defaultValue, new ConfigDescription(description, acceptableValues,"),
            "Native settings no longer delegate default/persistence handling to Config.Bind");
        Check(source.Contains("Language = nativeLanguage.Value,"),
            "Native options must use the saved language rather than re-detecting the OS language");
        Check(!Regex.IsMatch(source, @"nativeLanguage\.Value\s*=(?!=)|nativeLanguage\.BoxedValue\s*="),
            "Native initialization must not overwrite the saved language");
        Console.WriteLine("Native HUD language checks passed (primary tags, OS source, explicit choices, config binding).");
    }

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern ushort GetUserDefaultUILanguage();
}
