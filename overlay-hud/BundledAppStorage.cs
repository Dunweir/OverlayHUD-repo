using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace OverlayHUD
{
    // The caller supplies GetAppDirectory's result and serializes installs while the app is stopped.
    internal static class BundledAppStorage
    {
        private const string ExecutableName = "OverlayHUD.exe";
        private const string FingerprintName = ".overlayhud-archive.sha256";

        internal static string GetAppDirectory(string localAppData, string profileDirectory)
        {
            if (string.IsNullOrWhiteSpace(localAppData))
                throw new ArgumentException("Local application data directory is required.", nameof(localAppData));
            if (string.IsNullOrWhiteSpace(profileDirectory))
                throw new ArgumentException("Profile directory is required.", nameof(profileDirectory));

            string profile = FullPath(profileDirectory).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant();
            string identity;
            using (var sha = SHA256.Create())
                identity = Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(profile)));
            return Path.Combine(FullPath(localAppData), "OverlayHUD", "profiles", identity, "app");
        }

        internal static bool IsBundledExecutablePath(string configuredPath)
        {
            if (string.IsNullOrWhiteSpace(configuredPath)) return true;
            string path = configuredPath.Replace('\\', '/');
            // Match only literal relative defaults: rooted paths and custom paths remain user-owned.
            return string.Equals(path, "OverlayHUD_app/OverlayHUD.exe", StringComparison.OrdinalIgnoreCase)
                || string.Equals(path, "OverlayHUD.exe", StringComparison.OrdinalIgnoreCase)
                || string.Equals(path, "OverlayHUD-win32-x64/OverlayHUD.exe", StringComparison.OrdinalIgnoreCase)
                || string.Equals(path, "OverlayHUDApp/OverlayHUD.exe", StringComparison.OrdinalIgnoreCase)
                || string.Equals(path, "OverlayHUD_app/OverlayHUD-win32-x64/OverlayHUD.exe", StringComparison.OrdinalIgnoreCase);
        }

        internal static string EnsureInstalled(string appDirectory, string archivePath, string legacyDirectory, Action<string> logWarning)
        {
            if (string.IsNullOrWhiteSpace(appDirectory))
                throw new ArgumentException("Managed application directory is required.", nameof(appDirectory));
            string app = FullPath(appDirectory);
            string legacy = string.IsNullOrWhiteSpace(legacyDirectory) ? null : FullPath(legacyDirectory);
            if (legacy != null && (IsWithin(app, legacy) || IsWithin(legacy, app)))
                throw new IOException("Managed and legacy application directories must not overlap.");

            AssertSafeAncestors(app);
            bool appExists = Exists(app);
            if (appExists) AssertSafeTree(app);
            bool usable = appExists && IsUsable(app);
            bool migrate = false;
            if (legacy != null && Exists(legacy))
            {
                if (appExists)
                {
                    Warn(logWarning, "Both managed and legacy application paths exist; leaving legacy data untouched. Review and clean up manually: " + legacy);
                }
                else
                {
                    AssertSafeAncestors(legacy);
                    AssertSafeTree(legacy);
                    migrate = IsUsable(legacy);
                    if (!migrate)
                        Warn(logWarning, "Legacy path is not a recognized bundled installation; leaving it untouched: " + legacy);
                }
            }

            FileStream archive = null;
            try
            {
                string fingerprint = null;
                if (!string.IsNullOrWhiteSpace(archivePath))
                {
                    string zipPath = FullPath(archivePath);
                    AssertSafeAncestors(zipPath);
                    if (Exists(zipPath))
                    {
                        // Keep the same archive open for hashing and extraction, excluding writers on Windows.
                        archive = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                        using (var sha = SHA256.Create()) fingerprint = Hex(sha.ComputeHash(archive));
                        archive.Position = 0;
                    }
                }

                if (usable && (archive == null || ReadFingerprint(app) == fingerprint))
                    return Path.Combine(app, ExecutableName);
                if (archive == null && !migrate)
                    throw new FileNotFoundException("No usable bundled application or archive was found.", archivePath);

                string parent = Path.GetDirectoryName(app);
                Directory.CreateDirectory(parent);
                string stage = app + ".staging-" + Guid.NewGuid().ToString("N");
                try
                {
                    if (appExists) CopyTree(app, stage);
                    else if (migrate) CopyTree(legacy, stage);
                    else Directory.CreateDirectory(stage);

                    if (archive != null)
                    {
                        ExtractArchive(archive, stage);
                        File.WriteAllText(Path.Combine(stage, FingerprintName), fingerprint, Encoding.ASCII);
                    }
                    AssertSafeTree(stage);
                    if (!IsUsable(stage))
                        throw new InvalidDataException("Staged application lacks OverlayHUD.exe and Electron application resources.");

                    Promote(stage, app, appExists, logWarning);
                    // Only this invocation's complete migration authorizes deleting the original.
                    if (migrate) TryDeleteTree(legacy, logWarning);
                    return Path.Combine(app, ExecutableName);
                }
                finally
                {
                    TryDeleteTree(stage, logWarning);
                }
            }
            finally
            {
                if (archive != null) archive.Dispose();
            }
        }

        private static string FullPath(string path)
        {
            return Path.GetFullPath(path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar));
        }

        private static string Hex(byte[] bytes)
        {
            return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        }

        private static bool IsWithin(string path, string directory)
        {
            string root = directory.TrimEnd(Path.DirectorySeparatorChar);
            return string.Equals(path.TrimEnd(Path.DirectorySeparatorChar), root, StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        private static bool Exists(string path)
        {
            FileAttributes attributes;
            return TryGetAttributes(path, out attributes);
        }

        private static bool TryGetAttributes(string path, out FileAttributes attributes)
        {
            try { attributes = File.GetAttributes(path); return true; }
            catch (FileNotFoundException) { attributes = 0; return false; }
            catch (DirectoryNotFoundException) { attributes = 0; return false; }
        }

        private static void RejectReparsePoint(string path, FileAttributes attributes)
        {
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Refusing to follow or delete a reparse point: " + path);
        }

        private static void AssertSafeAncestors(string path)
        {
            for (string current = path; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            {
                FileAttributes attributes;
                if (TryGetAttributes(current, out attributes)) RejectReparsePoint(current, attributes);
            }
        }

        private static void AssertSafeTree(string directory)
        {
            FileAttributes attributes = File.GetAttributes(directory);
            RejectReparsePoint(directory, attributes);
            if ((attributes & FileAttributes.Directory) == 0)
                throw new IOException("Expected an application directory: " + directory);
            foreach (string entry in Directory.GetFileSystemEntries(directory))
            {
                attributes = File.GetAttributes(entry);
                RejectReparsePoint(entry, attributes);
                if ((attributes & FileAttributes.Directory) != 0) AssertSafeTree(entry);
            }
        }

        private static bool IsUsable(string directory)
        {
            return File.Exists(Path.Combine(directory, ExecutableName))
                && (File.Exists(Path.Combine(directory, "resources", "app.asar"))
                    || (File.Exists(Path.Combine(directory, "resources", "app", "package.json"))
                        && File.Exists(Path.Combine(directory, "resources", "app", "main.js"))));
        }

        private static string ReadFingerprint(string app)
        {
            string marker = Path.Combine(app, FingerprintName);
            return File.Exists(marker) ? File.ReadAllText(marker).Trim() : null;
        }

        private static void CopyTree(string source, string destination)
        {
            RejectReparsePoint(source, File.GetAttributes(source));
            Directory.CreateDirectory(destination);
            foreach (string entry in Directory.GetFileSystemEntries(source))
            {
                FileAttributes attributes = File.GetAttributes(entry);
                RejectReparsePoint(entry, attributes);
                string target = Path.Combine(destination, Path.GetFileName(entry));
                if ((attributes & FileAttributes.Directory) != 0) CopyTree(entry, target);
                else
                {
                    File.Copy(entry, target, false);
                    File.SetCreationTimeUtc(target, File.GetCreationTimeUtc(entry));
                    File.SetLastWriteTimeUtc(target, File.GetLastWriteTimeUtc(entry));
                }
            }
            Directory.SetCreationTimeUtc(destination, Directory.GetCreationTimeUtc(source));
            Directory.SetLastWriteTimeUtc(destination, Directory.GetLastWriteTimeUtc(source));
        }

        private static void ExtractArchive(Stream stream, string stage)
        {
            bool preserveConfig = Exists(Path.Combine(stage, "overlay-config.json"));
            bool preserveTimestamps = Exists(Path.Combine(stage, "timestamps"));
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Read, true))
            {
                HashSet<string> files = ValidateArchive(zip);
                if (!files.Contains("resources/app.asar"))
                {
                    // Electron prefers ASAR over unpacked code; never leave the old bundle active.
                    string asar = Path.Combine(stage, "resources", "app.asar");
                    if (Exists(asar)) File.Delete(asar);
                }
                else if (!(files.Contains("resources/app/package.json") && files.Contains("resources/app/main.js")))
                {
                    string unpacked = Path.Combine(stage, "resources", "app");
                    if (Directory.Exists(unpacked))
                    {
                        // Unpacked code may contain unknown user data. Retain the whole tree outside
                        // Electron's resource lookup paths rather than deleting it or reusing it later.
                        Directory.Move(unpacked, Path.Combine(stage, ".overlayhud-unpacked-backup-" + Guid.NewGuid().ToString("N")));
                    }
                }

                foreach (ZipArchiveEntry entry in zip.Entries)
                {
                    string name = entry.FullName.Replace('\\', '/');
                    if ((preserveConfig && IsArchiveChild(name, "overlay-config.json"))
                        || (preserveTimestamps && IsArchiveChild(name, "timestamps"))) continue;
                    string target = FullPath(Path.Combine(stage, name.Replace('/', Path.DirectorySeparatorChar)));
                    if (!IsWithin(target, stage) || string.Equals(target, stage, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Archive path escapes staging directory: " + entry.FullName);
                    if (name.EndsWith("/", StringComparison.Ordinal)) Directory.CreateDirectory(target);
                    else
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        using (Stream input = entry.Open())
                        using (var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None))
                            input.CopyTo(output);
                        File.SetLastWriteTimeUtc(target, entry.LastWriteTime.UtcDateTime);
                    }
                }
            }
        }

        private static HashSet<string> ValidateArchive(ZipArchive zip)
        {
            var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ZipArchiveEntry entry in zip.Entries)
            {
                string name = ValidateEntry(entry);
                if (!names.Add(name.TrimEnd('/')))
                    throw new InvalidDataException("Duplicate archive path: " + entry.FullName);
                if (!name.EndsWith("/", StringComparison.Ordinal)) files.Add(name);
            }
            // An old staged executable must not disguise an incomplete/corrupt replacement archive.
            if (!files.Contains(ExecutableName)
                || !(files.Contains("resources/app.asar")
                    || (files.Contains("resources/app/package.json") && files.Contains("resources/app/main.js"))))
                throw new InvalidDataException("Archive lacks a complete bundled Electron application at its root.");
            return files;
        }

        private static bool IsArchiveChild(string name, string root)
        {
            return string.Equals(name, root, StringComparison.OrdinalIgnoreCase)
                || name.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase);
        }

        private static string ValidateEntry(ZipArchiveEntry entry)
        {
            string name = entry.FullName.Replace('\\', '/');
            int unixType = (entry.ExternalAttributes >> 16) & 0xf000;
            if ((entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0
                || (unixType != 0 && unixType != 0x8000 && unixType != 0x4000))
                throw new InvalidDataException("Archive links and special files are not supported: " + entry.FullName);
            string[] parts = name.TrimEnd('/').Split('/');
            foreach (string part in parts)
            {
                if (part.Length == 0 || part == "." || part == ".." || part.EndsWith(".", StringComparison.Ordinal)
                    || part.EndsWith(" ", StringComparison.Ordinal) || part.IndexOfAny(new[] { ':', '<', '>', '"', '|', '?', '*' }) >= 0)
                    throw new InvalidDataException("Unsafe archive path: " + entry.FullName);
                foreach (char character in part)
                    if (character < 32) throw new InvalidDataException("Unsafe archive path: " + entry.FullName);
                string device = part.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
                if (device == "CON" || device == "PRN" || device == "AUX" || device == "NUL"
                    || (device.Length == 4 && (device.StartsWith("COM", StringComparison.Ordinal) || device.StartsWith("LPT", StringComparison.Ordinal))
                        && "123456789¹²³".IndexOf(device[3]) >= 0))
                    throw new InvalidDataException("Reserved Windows device path: " + entry.FullName);
            }
            if (IsArchiveChild(name, FingerprintName))
                throw new InvalidDataException("Archive contains the reserved installation marker.");
            return name;
        }

        private static void Promote(string stage, string app, bool appExists, Action<string> logWarning)
        {
            string backup = app + ".backup-" + Guid.NewGuid().ToString("N");
            if (appExists)
            {
                AssertSafeTree(app);
                Directory.Move(app, backup);
            }
            try { Directory.Move(stage, app); }
            catch (Exception promotionError)
            {
                if (appExists)
                {
                    try { Directory.Move(backup, app); }
                    catch (Exception rollbackError)
                    {
                        throw new AggregateException("Application promotion and rollback failed; original data remains at: " + backup,
                            promotionError, rollbackError);
                    }
                }
                throw;
            }
            if (appExists) TryDeleteTree(backup, logWarning);
        }

        private static void TryDeleteTree(string directory, Action<string> logWarning)
        {
            try
            {
                if (!Exists(directory)) return;
                AssertSafeAncestors(directory);
                AssertSafeTree(directory);
                DeleteTree(directory);
            }
            catch (Exception error)
            {
                Warn(logWarning, "Could not clean up application directory; retained data may need manual cleanup: " + directory + ". " + error.Message);
            }
        }

        private static void DeleteTree(string directory)
        {
            RejectReparsePoint(directory, File.GetAttributes(directory));
            foreach (string entry in Directory.GetFileSystemEntries(directory))
            {
                FileAttributes attributes = File.GetAttributes(entry);
                RejectReparsePoint(entry, attributes);
                if ((attributes & FileAttributes.Directory) != 0) DeleteTree(entry);
                else File.Delete(entry);
            }
            Directory.Delete(directory);
        }

        private static void Warn(Action<string> logWarning, string message)
        {
            // A logger failure must not mask an install error or undo a successful promotion.
            try { if (logWarning != null) logWarning(message); }
            catch { }
        }
    }
}
