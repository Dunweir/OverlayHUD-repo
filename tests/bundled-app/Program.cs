using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using OverlayHUD;

internal static class Program
{
    private const string Marker = ".overlayhud-archive.sha256";
    private static readonly DateTime OldTime = new DateTime(2020, 2, 3, 4, 5, 6, DateTimeKind.Utc);

    private static int Main(string[] args)
    {
        if (args.Length != 0)
        {
            try
            {
                Check(args.Length == 2 && args[0] == "--inspect-archive", "Usage: --inspect-archive <zip path>");
                InspectArchive(args[1]);
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error);
                return 1;
            }
        }
        var tests = new (string Name, Action<Fixture> Run)[]
        {
            ("Normalized, case-insensitive profile identities and isolation", PathIdentities),
            ("Only known relative executable aliases are bundled", Aliases),
            ("Fresh ZIP installation and SHA256 marker", FreshInstall),
            ("Same archive is idempotent", Idempotence),
            ("Archive update preserves settings, timestamps and unknown files", Update),
            ("ASAR to unpacked update removes only the obsolete ASAR", AsarToUnpacked),
            ("Unpacked to ASAR update preserves the old code tree outside resources", UnpackedToAsar),
            ("Failed ASAR to unpacked switch leaves the old tree intact", f => FormatSwitchFailure(f, true)),
            ("Failed unpacked to ASAR switch leaves the old tree intact", f => FormatSwitchFailure(f, false)),
            ("Recognized legacy tree migrates completely then is removed", Migration),
            ("Migration without archive supports unpacked Electron resources", UnpackedMigration),
            ("Archive upgrades migration without replacing user data", MigrationWithUpdate),
            ("Corrupt archive leaves existing installation and marker intact", CorruptUpdate),
            ("Incomplete archive cannot borrow validity from the old app", IncompleteUpdate),
            ("Extraction failure leaves existing installation intact", ExtractionFailure),
            ("Failed migration retains the complete legacy original", FailedMigration),
            ("ZIP traversal, Windows aliases and links are rejected", UnsafeArchives),
            ("Managed/legacy collision never merges or deletes legacy data", Collision),
            ("Unrecognized legacy data is retained", UnrecognizedLegacy),
            ("Existing invalid destination retains data and does not consume legacy", InvalidDestinationCollision),
            ("Missing archive permits existing usable app", MissingArchive),
            ("Missing or invalid install without archive throws", MissingEverything),
            ("Matching marker does not hide a broken installation", Repair),
            ("Locked migration cleanup warns and a retry preserves legacy data", LockedCleanup),
            ("Locked managed executable prevents destructive promotion", LockedPromotion),
            ("Legacy reparse points are rejected without touching their targets", LegacyReparsePoint),
            ("Managed tree and parent reparse points are rejected", ManagedReparsePoint),
            ("Overlapping managed and legacy directories are rejected", Overlap)
        };
        int failed = 0;
        foreach (var test in tests)
        {
            try
            {
                using (var fixture = new Fixture()) test.Run(fixture);
                Console.WriteLine("PASS " + test.Name);
            }
            catch (Exception error)
            {
                failed++;
                Console.Error.WriteLine("FAIL " + test.Name + Environment.NewLine + error);
            }
        }
        Console.WriteLine($"{tests.Length - failed}/{tests.Length} tests passed.");
        return failed == 0 ? 0 : 1;
    }

    private static void PathIdentities(Fixture f)
    {
        string profile = Path.Combine(f.Root, "Profiles", "Test Profile");
        string app = BundledAppStorage.GetAppDirectory(f.Local, profile);
        Equal(app, BundledAppStorage.GetAppDirectory(f.Local, profile.ToUpperInvariant() + "\\"));
        Equal(app, BundledAppStorage.GetAppDirectory(f.Local, profile.ToLowerInvariant() + "/"));
        Equal(app, BundledAppStorage.GetAppDirectory(f.Local, profile.Replace('\\', '/')));
        Equal(app, BundledAppStorage.GetAppDirectory(f.Local, Path.Combine(profile, "child", "..")));
        Equal(app, BundledAppStorage.GetAppDirectory(f.Local, Path.GetRelativePath(Environment.CurrentDirectory, profile)));
        Check(app != BundledAppStorage.GetAppDirectory(f.Local, profile + " Other"), "Profiles must not share data.");
        Check(Path.IsPathFullyQualified(app), "Storage path must be absolute.");
        Equal("app", Path.GetFileName(app));
        string identity = Path.GetFileName(Path.GetDirectoryName(app));
        Check(identity.Length == 64 && identity.All(c => "0123456789abcdef".Contains(c)), "Profile identity must be SHA256 hex.");
        Equal(Path.Combine(Path.GetFullPath(f.Local), "OverlayHUD", "profiles", identity, "app"), app);
        Throws<ArgumentException>(() => BundledAppStorage.GetAppDirectory("", profile));
        Throws<ArgumentException>(() => BundledAppStorage.GetAppDirectory(" \t", profile));
        Throws<ArgumentException>(() => BundledAppStorage.GetAppDirectory(null, profile));
        Throws<ArgumentException>(() => BundledAppStorage.GetAppDirectory(f.Local, ""));
    }

    private static void Aliases(Fixture f)
    {
        foreach (string alias in new[] { "", " ", "OverlayHUD_app/OverlayHUD.exe", "OverlayHUD.exe",
            "OverlayHUD-win32-x64/OverlayHUD.exe", "OverlayHUDApp/OverlayHUD.exe", "OverlayHUD_app/OverlayHUD-win32-x64/OverlayHUD.exe" })
        {
            Check(BundledAppStorage.IsBundledExecutablePath(alias), "Expected alias: " + alias);
            Check(BundledAppStorage.IsBundledExecutablePath(alias.Replace('/', '\\').ToUpperInvariant()), "Expected Windows alias: " + alias);
        }
        Check(BundledAppStorage.IsBundledExecutablePath(null), "Null uses bundled default.");
        foreach (string custom in new[] { "MyApp/OverlayHUD.exe", "../OverlayHUD.exe", "./OverlayHUD.exe", "/OverlayHUD.exe",
            "\\OverlayHUD.exe", "C:\\OverlayHUD.exe", "C:OverlayHUD.exe", "\\\\server\\share\\OverlayHUD.exe",
            "OverlayHUD_app/../OverlayHUD.exe", "OverlayHUD.exe.bak", "OverlayHUD.exe ", Path.Combine(f.Root, "OverlayHUD_app", "OverlayHUD.exe") })
            Check(!BundledAppStorage.IsBundledExecutablePath(custom), "Must retain custom path: " + custom);
    }

    private static void FreshInstall(Fixture f)
    {
        f.Zip("v1", ("overlay-config.json", "defaults"), ("assets/image.txt", "asset"));
        Equal(Path.Combine(f.App, "OverlayHUD.exe"), f.Install());
        Equal("v1", Read(f.App, "OverlayHUD.exe"));
        Equal("asar-v1", Read(f.App, "resources/app.asar"));
        Equal("defaults", Read(f.App, "overlay-config.json"));
        Equal("asset", Read(f.App, "assets/image.txt"));
        Equal(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f.Archive))).ToLowerInvariant(), Read(f.App, Marker));
        f.NoTransactionDirectories();
    }

    private static void Idempotence(Fixture f)
    {
        f.Zip("v1");
        f.Install();
        Write(f.App, "OverlayHUD.exe", "local sentinel");
        Write(f.App, "unknown/notes.txt", "keep");
        var before = Snapshot(f.App);
        DateTime directoryTime = Directory.GetLastWriteTimeUtc(f.App);
        f.Install();
        SameSnapshot(before, f.App);
        Equal(directoryTime, Directory.GetLastWriteTimeUtc(f.App));
        f.NoTransactionDirectories();
    }

    private static void Update(Fixture f)
    {
        f.Zip("v1", ("overlay-config.json", "default"), ("old-resource.txt", "old"));
        f.Install();
        UserData(f.App);
        var userFiles = UserSnapshot(f.App);
        string fingerprint = Read(f.App, Marker);
        f.Zip("v2", ("overlay-config.json", "new defaults"), ("timestamps/run.txt", "must not replace"),
            ("timestamps/packaged.txt", "must not add to existing timestamps"), ("new-resource.txt", "new"));
        f.Install();
        Equal("v2", Read(f.App, "OverlayHUD.exe"));
        Equal("asar-v2", Read(f.App, "resources/app.asar"));
        Equal("new", Read(f.App, "new-resource.txt"));
        Equal("old", Read(f.App, "old-resource.txt"));
        SameFiles(userFiles, f.App);
        Check(!File.Exists(Path.Combine(f.App, "timestamps", "packaged.txt")), "Existing timestamp tree must be preserved.");
        Check(fingerprint != Read(f.App, Marker), "Changed archive must update its marker.");
        f.NoTransactionDirectories();
    }

    private static void AsarToUnpacked(Fixture f)
    {
        f.Zip("v1");
        f.Install();
        UserData(f.App);
        Write(f.App, "resources/unrelated/user.dat", "unrelated resource data");
        Write(f.App, "resources/app.asar.unpacked/user.dat", "retain inert companion data");
        var preserved = Snapshot(f.App);
        preserved.Remove("OverlayHUD.exe");
        preserved.Remove(Path.Combine("resources", "app.asar"));
        preserved.Remove(Marker);
        f.RawZip(("OverlayHUD.exe", "v2"), ("resources/app/package.json", "{}"),
            ("resources/app/main.js", "new unpacked code"), ("overlay-config.json", "defaults"));
        f.Install();
        Check(!File.Exists(Path.Combine(f.App, "resources", "app.asar")), "Old ASAR must not shadow new unpacked code.");
        Equal("new unpacked code", Read(f.App, "resources/app/main.js"));
        Equal("v2", Read(f.App, "OverlayHUD.exe"));
        SameFiles(preserved, f.App);
        var installed = Snapshot(f.App);
        f.Install();
        SameSnapshot(installed, f.App);
        f.NoTransactionDirectories();
    }

    private static void UnpackedToAsar(Fixture f)
    {
        f.RawZip(("OverlayHUD.exe", "v1"), ("resources/app/package.json", "{}"), ("resources/app/main.js", "old unpacked code"));
        f.Install();
        Write(f.App, "resources/app/unknown/notes.txt", "user data inside old code tree");
        Write(f.App, "resources/unrelated/user.dat", "unrelated resource data");
        Directory.CreateDirectory(Path.Combine(f.App, "resources", "app", "empty-user-directory"));
        UserData(f.App);
        var oldCode = Snapshot(Path.Combine(f.App, "resources", "app"));
        var preserved = UserSnapshot(f.App);
        f.Zip("v2");
        f.Install();
        Check(!Directory.Exists(Path.Combine(f.App, "resources", "app")), "Old unpacked code must leave Electron's active resource paths.");
        Equal("asar-v2", Read(f.App, "resources/app.asar"));
        Equal("unrelated resource data", Read(f.App, "resources/unrelated/user.dat"));
        SameFiles(preserved, f.App);
        string[] backups = Directory.GetDirectories(f.App, ".overlayhud-unpacked-backup-*");
        Equal(1, backups.Length);
        SameSnapshot(oldCode, backups[0]);
        Check(Directory.Exists(Path.Combine(backups[0], "empty-user-directory")), "Backup must preserve empty user directories.");
        var installed = Snapshot(f.App);
        f.Install();
        SameSnapshot(installed, f.App);
        Equal(1, Directory.GetDirectories(f.App, ".overlayhud-unpacked-backup-*").Length);

        // Subsequent switches must neither reactivate old code nor overwrite an earlier backup.
        f.RawZip(("OverlayHUD.exe", "v3"), ("resources/app/package.json", "{}"), ("resources/app/main.js", "latest unpacked code"));
        f.Install();
        Check(!File.Exists(Path.Combine(f.App, "resources", "app.asar")), "Repeated switch must remove ASAR again.");
        Check(!Directory.Exists(Path.Combine(f.App, "resources", "app", "unknown")), "Old backup must not become active again.");
        f.Zip("v4");
        f.Install();
        Equal(2, Directory.GetDirectories(f.App, ".overlayhud-unpacked-backup-*").Length);
        SameSnapshot(oldCode, backups[0]);
        Equal("asar-v4", Read(f.App, "resources/app.asar"));
        f.NoTransactionDirectories();
    }

    private static void FormatSwitchFailure(Fixture f, bool fromAsar)
    {
        if (fromAsar) f.Zip("v1");
        else f.RawZip(("OverlayHUD.exe", "v1"), ("resources/app/package.json", "{}"), ("resources/app/main.js", "old code"));
        f.Install();
        UserData(f.App);
        Write(f.App, "user-file", "not a directory");
        var before = Snapshot(f.App);
        if (fromAsar)
            f.RawZip(("OverlayHUD.exe", "v2"), ("resources/app/package.json", "{}"), ("resources/app/main.js", "new code"),
                ("user-file/child.txt", "conflict after staged format change"));
        else f.Zip("v2", ("user-file/child.txt", "conflict after staged format change"));
        Throws<IOException>(() => f.Install());
        SameSnapshot(before, f.App);
        Equal(0, Directory.GetDirectories(f.App, ".overlayhud-unpacked-backup-*").Length);
        f.NoTransactionDirectories();
    }

    private static void InspectArchive(string path)
    {
        using (var zip = ZipFile.OpenRead(path))
        {
            // Exercise the installer's actual metadata validation without extracting or altering the ZIP.
            MethodInfo validate = typeof(BundledAppStorage).GetMethod("ValidateArchive", BindingFlags.NonPublic | BindingFlags.Static);
            var files = (HashSet<string>)validate.Invoke(null, new object[] { zip });
            Console.WriteLine("Validated read-only: " + Path.GetFullPath(path));
            Console.WriteLine("Entries: " + zip.Entries.Count);
            foreach (string entry in new[] { "OverlayHUD.exe", "resources/app.asar", "resources/app/package.json", "resources/app/main.js" })
                Console.WriteLine(entry + ": " + (files.Contains(entry) ? "present" : "absent"));
        }
    }

    private static void Migration(Fixture f)
    {
        InstallTree(f.Legacy, "legacy");
        UserData(f.Legacy);
        Directory.CreateDirectory(Path.Combine(f.Legacy, "unknown-empty"));
        var before = Snapshot(f.Legacy);
        f.Install();
        SameSnapshot(before, f.App);
        Check(Directory.Exists(Path.Combine(f.App, "unknown-empty")), "Empty directories also migrate.");
        Check(!Directory.Exists(f.Legacy), "Fully copied legacy directory must be removed.");
        f.NoTransactionDirectories();
    }

    private static void UnpackedMigration(Fixture f)
    {
        Write(f.Legacy, "OverlayHUD.exe", "legacy");
        Write(f.Legacy, "resources/app/package.json", "{}");
        Write(f.Legacy, "resources/app/main.js", "main");
        UserData(f.Legacy);
        f.Install();
        Equal("main", Read(f.App, "resources/app/main.js"));
        Equal("settings", Read(f.App, "overlay-config.json"));
        Check(!Directory.Exists(f.Legacy), "Unpacked Electron installation must migrate.");
    }

    private static void MigrationWithUpdate(Fixture f)
    {
        InstallTree(f.Legacy, "legacy");
        UserData(f.Legacy);
        var userFiles = UserSnapshot(f.Legacy);
        f.Zip("new", ("overlay-config.json", "defaults"), ("timestamps/run.txt", "defaults"));
        f.Install();
        Equal("new", Read(f.App, "OverlayHUD.exe"));
        SameFiles(userFiles, f.App);
        Check(!Directory.Exists(f.Legacy), "Legacy cleanup must follow successful promotion.");
    }

    private static void CorruptUpdate(Fixture f)
    {
        f.Zip("v1");
        f.Install();
        UserData(f.App);
        var before = Snapshot(f.App);
        File.WriteAllText(f.Archive, "not a zip");
        Throws<InvalidDataException>(() => f.Install());
        SameSnapshot(before, f.App);
        f.NoTransactionDirectories();
    }

    private static void IncompleteUpdate(Fixture f)
    {
        f.Zip("v1");
        f.Install();
        UserData(f.App);
        var before = Snapshot(f.App);
        foreach (var entries in new[] { Array.Empty<(string, string)>(), new[] { ("OverlayHUD.exe", "partial") }, new[] { ("resources/app.asar", "partial") } })
        {
            f.RawZip(entries);
            Throws<InvalidDataException>(() => f.Install());
            SameSnapshot(before, f.App);
            f.NoTransactionDirectories();
        }
    }

    private static void ExtractionFailure(Fixture f)
    {
        f.Zip("v1");
        f.Install();
        UserData(f.App);
        Write(f.App, "user-file", "not a directory");
        var before = Snapshot(f.App);
        f.Zip("v2", ("user-file/child.txt", "conflict after exe overwrite"));
        Throws<IOException>(() => f.Install());
        SameSnapshot(before, f.App);
        f.NoTransactionDirectories();
    }

    private static void FailedMigration(Fixture f)
    {
        InstallTree(f.Legacy, "legacy");
        UserData(f.Legacy);
        var before = Snapshot(f.Legacy);
        File.WriteAllText(f.Archive, "broken archive");
        Throws<InvalidDataException>(() => f.Install());
        SameSnapshot(before, f.Legacy);
        Check(!Directory.Exists(f.App), "Failed migration must not promote an app.");
        f.NoTransactionDirectories();
        f.Zip("fixed");
        f.Install();
        Equal("fixed", Read(f.App, "OverlayHUD.exe"));
        Equal("settings", Read(f.App, "overlay-config.json"));
        Check(!Directory.Exists(f.Legacy), "Retry must finish migration.");
    }

    private static void UnsafeArchives(Fixture f)
    {
        f.Zip("v1");
        f.Install();
        UserData(f.App);
        var before = Snapshot(f.App);
        foreach (string unsafePath in new[] { "../escape.txt", "..\\escape.txt", "safe/../../escape.txt", "/absolute.txt",
            "\\absolute.txt", "C:/escape.txt", "C:escape.txt", "\\\\host\\share\\escape.txt", "name:stream", "dir/../escape.txt",
            "dir./file.txt", "dir /file.txt", "CON.txt", "NUL", "COM1.log", "LPT¹", Marker, "Resources/App.asar" })
        {
            f.Zip("bad", (unsafePath, "attack"));
            Throws<InvalidDataException>(() => f.Install());
            SameSnapshot(before, f.App);
            Check(!File.Exists(Path.Combine(Path.GetDirectoryName(f.App), "escape.txt")), "Traversal must not write outside staging.");
            f.NoTransactionDirectories();
        }
        f.Zip("bad");
        using (var zip = ZipFile.Open(f.Archive, ZipArchiveMode.Update))
        {
            ZipArchiveEntry entry = zip.CreateEntry("link");
            entry.ExternalAttributes = unchecked((int)0xa1ff0000);
            using (var writer = new StreamWriter(entry.Open())) writer.Write("../outside");
        }
        Throws<InvalidDataException>(() => f.Install());
        SameSnapshot(before, f.App);
        f.NoTransactionDirectories();
    }

    private static void Collision(Fixture f)
    {
        f.Zip("v1");
        f.Install();
        UserData(f.App);
        InstallTree(f.Legacy, "old");
        UserData(f.Legacy);
        Write(f.Legacy, "overlay-config.json", "old settings");
        Write(f.Legacy, "never-copied.txt", "old only");
        var old = Snapshot(f.Legacy);
        var current = Snapshot(f.App);
        f.Install();
        SameSnapshot(current, f.App);
        SameSnapshot(old, f.Legacy);
        f.Zip("v2");
        f.Install();
        SameSnapshot(old, f.Legacy);
        Equal("settings", Read(f.App, "overlay-config.json"));
        Check(!File.Exists(Path.Combine(f.App, "never-copied.txt")), "Do not silently merge a collision.");
        Check(f.Warnings.Any(w => w.Contains("manually")), "Collision must warn about manual cleanup.");
    }

    private static void UnrecognizedLegacy(Fixture f)
    {
        Write(f.Legacy, "OverlayHUD.exe", "custom executable, no Electron resources");
        UserData(f.Legacy);
        var old = Snapshot(f.Legacy);
        f.Zip("bundled");
        f.Install();
        SameSnapshot(old, f.Legacy);
        Check(!File.Exists(Path.Combine(f.App, "overlay-config.json")), "Unrecognized legacy folder must not be imported.");
        Check(f.Warnings.Count != 0, "Unrecognized legacy folder must warn.");
    }

    private static void InvalidDestinationCollision(Fixture f)
    {
        UserData(f.App);
        InstallTree(f.Legacy, "old");
        Write(f.Legacy, "never-copied.txt", "old");
        var old = Snapshot(f.Legacy);
        var current = Snapshot(f.App);
        Throws<FileNotFoundException>(() => f.Install());
        SameSnapshot(current, f.App);
        SameSnapshot(old, f.Legacy);
        f.Zip("new");
        f.Install();
        Equal("settings", Read(f.App, "overlay-config.json"));
        SameSnapshot(old, f.Legacy);
        Check(!File.Exists(Path.Combine(f.App, "never-copied.txt")), "Existing destination prevents legacy migration even if not yet usable.");
    }

    private static void MissingArchive(Fixture f)
    {
        InstallTree(f.App, "existing without marker");
        UserData(f.App);
        var before = Snapshot(f.App);
        Equal(Path.Combine(f.App, "OverlayHUD.exe"), f.Install());
        Equal(Path.Combine(f.App, "OverlayHUD.exe"), BundledAppStorage.EnsureInstalled(f.App, null, null, null));
        SameSnapshot(before, f.App);
        f.Zip("updated");
        f.Install();
        File.Delete(f.Archive);
        Equal(Path.Combine(f.App, "OverlayHUD.exe"), f.Install());
        Equal("updated", Read(f.App, "OverlayHUD.exe"));
    }

    private static void MissingEverything(Fixture f)
    {
        Throws<FileNotFoundException>(() => f.Install());
        Check(!Directory.Exists(f.App), "Missing archive must not create an empty app.");
        Write(f.Legacy, "OverlayHUD.exe", "unrecognized");
        Write(f.Legacy, "resources/app/package.json", "missing main.js");
        Throws<FileNotFoundException>(() => f.Install());
        Check(File.Exists(Path.Combine(f.Legacy, "OverlayHUD.exe")), "Unrecognized legacy executable must remain.");
        Write(f.App, "OverlayHUD.exe", "missing resources");
        Throws<FileNotFoundException>(() => f.Install());
    }

    private static void Repair(Fixture f)
    {
        f.Zip("v1");
        f.Install();
        UserData(f.App);
        File.Delete(Path.Combine(f.App, "resources", "app.asar"));
        f.Install();
        Equal("asar-v1", Read(f.App, "resources/app.asar"));
        Equal("settings", Read(f.App, "overlay-config.json"));
    }

    private static void LockedCleanup(Fixture f)
    {
        if (!OperatingSystem.IsWindows()) { Console.WriteLine("  Windows file-lock test not applicable on this OS."); return; }
        InstallTree(f.Legacy, "legacy");
        UserData(f.Legacy);
        using (var locked = new FileStream(Path.Combine(f.Legacy, "OverlayHUD.exe"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            f.Install();
            Equal("legacy", Read(f.App, "OverlayHUD.exe"));
            Equal("settings", Read(f.App, "overlay-config.json"));
            Check(Directory.Exists(f.Legacy), "Locked cleanup must retain legacy directory.");
            Check(f.Warnings.Any(w => w.Contains("Could not clean up")), "Locked cleanup must warn.");
        }
        Write(f.Legacy, "uncopied-after-failure.txt", "retain on retry");
        Write(f.App, "overlay-config.json", "new external settings");
        var remaining = Snapshot(f.Legacy);
        f.Install();
        SameSnapshot(remaining, f.Legacy);
        Equal("new external settings", Read(f.App, "overlay-config.json"));
    }

    private static void LockedPromotion(Fixture f)
    {
        if (!OperatingSystem.IsWindows()) { Console.WriteLine("  Windows file-lock test not applicable on this OS."); return; }
        f.Zip("v1");
        f.Install();
        UserData(f.App);
        var before = Snapshot(f.App);
        f.Zip("v2");
        using (var locked = new FileStream(Path.Combine(f.App, "OverlayHUD.exe"), FileMode.Open, FileAccess.Read, FileShare.Read))
            Throws<IOException>(() => f.Install());
        SameSnapshot(before, f.App);
        f.NoTransactionDirectories();
    }

    private static void LegacyReparsePoint(Fixture f)
    {
        InstallTree(f.Legacy, "legacy");
        string outside = Path.Combine(f.Root, "outside");
        Write(outside, "valuable.txt", "do not touch");
        string link = Path.Combine(f.Legacy, "unknown-link");
        CreateDirectoryLink(link, outside);
        try
        {
            f.Zip("v2");
            Throws<IOException>(() => f.Install());
            Equal("do not touch", Read(outside, "valuable.txt"));
            Equal("legacy", Read(f.Legacy, "OverlayHUD.exe"));
            Check(!Directory.Exists(f.App), "Reparse point must prevent migration.");
        }
        finally { Directory.Delete(link); }
    }

    private static void ManagedReparsePoint(Fixture f)
    {
        f.Zip("v1");
        f.Install();
        string outside = Path.Combine(f.Root, "outside");
        Write(outside, "valuable.txt", "do not touch");
        string link = Path.Combine(f.App, "user-link");
        CreateDirectoryLink(link, outside);
        try
        {
            Throws<IOException>(() => f.Install());
            File.Delete(f.Archive);
            Throws<IOException>(() => f.Install());
            Equal("do not touch", Read(outside, "valuable.txt"));
        }
        finally { Directory.Delete(link); }

        string parentLink = Path.Combine(f.Root, "local-link");
        CreateDirectoryLink(parentLink, outside);
        try
        {
            string app = BundledAppStorage.GetAppDirectory(parentLink, f.Legacy);
            f.Zip("v2");
            Throws<IOException>(() => BundledAppStorage.EnsureInstalled(app, f.Archive, null, f.Warnings.Add));
            Check(!Directory.Exists(Path.Combine(outside, "OverlayHUD")), "Reparse parent must not redirect installation.");
        }
        finally { Directory.Delete(parentLink); }
    }

    private static void Overlap(Fixture f)
    {
        InstallTree(f.App, "existing");
        var before = Snapshot(f.App);
        foreach (string legacy in new[] { f.App, Path.GetDirectoryName(f.App), Path.Combine(f.App, "child") })
            Throws<IOException>(() => BundledAppStorage.EnsureInstalled(f.App, f.Archive, legacy, null));
        SameSnapshot(before, f.App);
    }

    private static void CreateDirectoryLink(string link, string target)
    {
        if (!OperatingSystem.IsWindows()) { Directory.CreateSymbolicLink(link, target); return; }
        // Junctions exercise ReparsePoint handling without requiring symlink privileges or developer mode.
        var start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in new[] { "/c", "mklink", "/J", link, target }) start.ArgumentList.Add(argument);
        using (var process = Process.Start(start))
        {
            string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            Check(process.WaitForExit(10000) && process.ExitCode == 0, "Cannot create test junction: " + output);
        }
    }

    private static void InstallTree(string root, string version)
    {
        Write(root, "OverlayHUD.exe", version);
        Write(root, "resources/app.asar", "asar-" + version);
    }

    private static void UserData(string root)
    {
        Write(root, "overlay-config.json", "settings");
        Write(root, "timestamps/run.txt", "session history");
        Write(root, "timestamps/nested/run.txt", "nested session history");
        Write(root, "unknown/user.dat", "unknown user bytes");
        foreach (string path in Directory.GetFiles(root, "*", SearchOption.AllDirectories)) File.SetLastWriteTimeUtc(path, OldTime);
    }

    private static Dictionary<string, (string Content, DateTime Modified)> Snapshot(string root)
    {
        return Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(
            file => Path.GetRelativePath(root, file), file => (Convert.ToBase64String(File.ReadAllBytes(file)), File.GetLastWriteTimeUtc(file)));
    }

    private static Dictionary<string, (string Content, DateTime Modified)> UserSnapshot(string root)
    {
        return Snapshot(root).Where(pair => pair.Key == "overlay-config.json" || pair.Key.StartsWith("timestamps") || pair.Key.StartsWith("unknown"))
            .ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    private static void SameSnapshot(Dictionary<string, (string Content, DateTime Modified)> before, string root)
    {
        Equal(before.Count, Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length);
        SameFiles(before, root);
    }

    private static void SameFiles(Dictionary<string, (string Content, DateTime Modified)> before, string root)
    {
        foreach (var pair in before)
        {
            string file = Path.Combine(root, pair.Key);
            Check(File.Exists(file), "Missing preserved file: " + file);
            Equal(pair.Value.Content, Convert.ToBase64String(File.ReadAllBytes(file)));
            Equal(pair.Value.Modified, File.GetLastWriteTimeUtc(file));
        }
    }

    private static void Write(string root, string relative, string text)
    {
        string path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, text);
    }

    private static string Read(string root, string relative) => File.ReadAllText(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception($"Expected {expected}, got {actual}.");
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name + ".");
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly string Root = Path.Combine(Path.GetTempPath(), "OverlayHUD-BundledAppTests-" + Guid.NewGuid().ToString("N"));
        internal readonly string Local;
        internal readonly string App;
        internal readonly string Archive;
        internal readonly string Legacy;
        internal readonly List<string> Warnings = new List<string>();

        internal Fixture()
        {
            Local = Path.Combine(Root, "LocalAppData");
            Legacy = Path.Combine(Root, "profile", "plugins", "OverlayHUD_app");
            Archive = Path.Combine(Root, "OverlayHUD_app.zip");
            App = BundledAppStorage.GetAppDirectory(Local, Path.Combine(Root, "profile"));
            Directory.CreateDirectory(Root);
        }

        internal string Install() => BundledAppStorage.EnsureInstalled(App, Archive, Legacy, Warnings.Add);

        internal void Zip(string version, params (string Name, string Text)[] extra)
        {
            RawZip(new[] { ("OverlayHUD.exe", version), ("resources/app.asar", "asar-" + version) }.Concat(extra).ToArray());
        }

        internal void RawZip(params (string Name, string Text)[] entries)
        {
            using (var file = new FileStream(Archive, FileMode.Create, FileAccess.Write))
            using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
                foreach (var entry in entries)
                    using (var writer = new StreamWriter(zip.CreateEntry(entry.Name).Open(), Encoding.UTF8)) writer.Write(entry.Text);
        }

        internal void NoTransactionDirectories()
        {
            string parent = Path.GetDirectoryName(App);
            if (Directory.Exists(parent))
                Check(!Directory.GetDirectories(parent).Any(d => Path.GetFileName(d).StartsWith("app.")), "Staging/backup directory leaked.");
        }

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }
    }
}
