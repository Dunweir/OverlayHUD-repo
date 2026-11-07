using System;
using System.Globalization;
using System.Runtime.InteropServices;

namespace OverlayHUD
{
    internal static class NativeHudLanguage
    {
        internal static readonly string DefaultLanguage = ReadSystemLanguage();

        internal static string FromCulture(CultureInfo culture) =>
            string.Equals(culture?.TwoLetterISOLanguageName, "ru", StringComparison.OrdinalIgnoreCase) ? "ru" : "en";

        internal static string ReadSystemLanguage()
        {
            try
            {
                // CurrentUICulture can be changed by the game/mods; InstalledUICulture on Windows
                // is the install language, not necessarily the current user's display language.
                // Query Windows directly rather than relying on Unity's locale mapping.
                var culture = Environment.OSVersion.Platform == PlatformID.Win32NT
                    ? CultureInfo.GetCultureInfo(GetUserDefaultUILanguage())
                    : CultureInfo.InstalledUICulture;
                return FromCulture(culture);
            }
            catch (CultureNotFoundException) { return "en"; }
            catch (DllNotFoundException) { return "en"; }
            catch (EntryPointNotFoundException) { return "en"; }
        }

        [DllImport("kernel32.dll", ExactSpelling = true)]
        private static extern ushort GetUserDefaultUILanguage();
    }
}
