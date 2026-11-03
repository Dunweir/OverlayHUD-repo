using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace OverlayHUD
{
    // State is sampled on the caller's thread; only immutable text reaches the disk worker.
    internal sealed class NativeTimestampWriter : IDisposable
    {
        private readonly NativeHudState state;
        private readonly string directory;
        private readonly Action<Exception> logError;
        private readonly Func<DateTime> localNow;
        private readonly List<string> lines = new List<string>();
        private int activeLine = -1;
        private bool disposed;
        private DateTime sessionStart;
        private Task pending = Task.CompletedTask;
        // The path is owned by the serialized disk worker; failure also stops caller-side recording.
        private string filePath;
        private volatile bool failed;

        internal NativeTimestampWriter(NativeHudState state, string directory, Action<Exception> logError,
            Func<DateTime> localNow = null)
        {
            this.state = state;
            this.directory = directory;
            this.logError = logError;
            this.localNow = localNow ?? (() => DateTime.Now);
        }

        // Must run BEFORE NativeHudState.Apply clears the previous level's monsters.
        internal void BeforeLevel(string json, double now, bool save, string language)
        {
            if (disposed || failed) return;
            JObject data = JObject.Parse(json);
            JToken token = data["level"];
            string text = token?.Type == JTokenType.String ? (string)token : token?.ToString(Newtonsoft.Json.Formatting.None);
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double level)
                || double.IsNaN(level) || level < 1 || level > int.MaxValue) return;

            End(now);
            if (!save) return;
            DateTime start = localNow();
            if (lines.Count == 0) sessionStart = start;
            string map = data["levelName"]?.Type == JTokenType.String ? (string)data["levelName"] : "";
            string label = language == "en" ? "Level" : "Уровень";
            lines.Add(start.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + " " + label + " "
                + Math.Floor(level).ToString(CultureInfo.InvariantCulture) + " "
                + (string.IsNullOrWhiteSpace(map) ? "?" : map.Replace('\r', ' ').Replace('\n', ' ').Trim()));
            activeLine = lines.Count - 1;
            QueueWrite();
        }

        internal void Tick(double now, bool save)
        {
            // Hidden (map/quota UI) and renderer preferences do not end gameplay.
            if (!save || !state.Visible) End(now);
        }

        internal void End(double now)
        {
            if (activeLine < 0) return;
            if (failed) { activeLine = -1; return; }
            var totals = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (NativeHudState.Monster monster in state.GetDisplayMonsters(now))
            {
                if (!monster.Seen || string.IsNullOrWhiteSpace(monster.Name)) continue;
                totals.TryGetValue(monster.Name, out int count);
                totals[monster.Name] = count + Math.Max(1, monster.Count);
            }
            var names = new List<string>();
            foreach (var monster in totals)
                names.Add(monster.Key.Replace('\r', ' ').Replace('\n', ' ')
                    + (monster.Value > 1 ? " x" + monster.Value.ToString(CultureInfo.InvariantCulture) : ""));
            if (names.Count > 0) lines[activeLine] += " [" + string.Join(", ", names) + "]";
            activeLine = -1;
            QueueWrite();
        }

        private void QueueWrite()
        {
            string content = string.Join(Environment.NewLine, lines) + Environment.NewLine;
            DateTime start = sessionStart;
            pending = pending.ContinueWith(_ => WriteSnapshot(content, start), TaskScheduler.Default);
        }

        private void WriteSnapshot(string content, DateTime start)
        {
            if (failed) return;
            try
            {
                if (filePath == null)
                {
                    Directory.CreateDirectory(directory);
                    string stem = start.ToString("yyyy-MM-dd_HH-mm-ss-fff", CultureInfo.InvariantCulture);
                    for (int suffix = 0; ; suffix++)
                    {
                        string candidate = Path.Combine(directory, stem
                            + (suffix == 0 ? "" : "_" + suffix.ToString(CultureInfo.InvariantCulture)) + ".txt");
                        FileStream stream;
                        try
                        {
                            // CreateNew reserves the name atomically, including concurrent sessions.
                            stream = new FileStream(candidate, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                        }
                        catch (IOException) when (File.Exists(candidate))
                        {
                            continue;
                        }
                        // Do not mistake a write failure after creation for a filename collision.
                        using (stream)
                        using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                            writer.Write(content);
                        filePath = candidate;
                        return;
                    }
                }
                File.WriteAllText(filePath, content, new UTF8Encoding(false));
            }
            catch (Exception error)
            {
                failed = true;
                logError?.Invoke(error);
            }
        }

        internal Task FlushAsync() => pending;

        public void Dispose()
        {
            if (disposed) return;
            End(0); // Names/counts do not depend on the countdown clock.
            disposed = true;
            // Do not wait for disk IO on Unity's thread. FlushAsync is available to hosts/tests.
        }
    }
}
