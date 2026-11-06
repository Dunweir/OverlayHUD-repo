// Minimal API doubles for executing the actual localization coroutine without Unity.
using System.Collections;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace UnityEngine
{
    public sealed class WaitForSecondsRealtime
    {
        public readonly float Seconds;
        public WaitForSecondsRealtime(float seconds) { Seconds = seconds; }
    }
}

namespace UnityEngine.ResourceManagement.AsyncOperations
{
    public enum AsyncOperationStatus { None, Succeeded, Failed }
    public struct AsyncOperationHandle<T>
    {
        public AsyncOperationStatus Status;
        public T Result;
    }
}

namespace UnityEngine.Localization.Tables
{
    public sealed class StringTable
    {
        public IEnumerable<KeyValuePair<string, string>> Values;
    }
}

namespace UnityEngine.Localization.Settings
{
    public sealed class Locale
    {
        public string Code;
    }
    public sealed class Locales
    {
        public readonly HashSet<string> Missing = new();
        public Locale GetLocale(string code) => Missing.Contains(code) ? null : new Locale { Code = code };
    }
    public sealed class StringDatabase
    {
        public readonly List<string> Requests = new();
        public readonly Dictionary<string, int> Failures = new();
        public readonly Dictionary<string, StringTable> Tables = new();
        public readonly HashSet<string> Throw = new();
        public AsyncOperationHandle<StringTable> GetTableAsync(string name, Locale locale)
        {
            if (name != "HUD") throw new Exception("Unexpected table: " + name);
            Requests.Add(locale.Code);
            if (Throw.Contains(locale.Code)) throw new InvalidOperationException("Unavailable Addressables");
            if (Failures.TryGetValue(locale.Code, out int remaining) && remaining > 0)
            {
                Failures[locale.Code] = remaining - 1;
                return new AsyncOperationHandle<StringTable> { Status = AsyncOperationStatus.Failed };
            }
            return new AsyncOperationHandle<StringTable>
            {
                Status = AsyncOperationStatus.Succeeded, Result = Tables.GetValueOrDefault(locale.Code)
            };
        }
    }
    public sealed class LocalizationSettings
    {
        public static bool ThrowInitialization;
        public static AsyncOperationStatus InitializationStatus = AsyncOperationStatus.Succeeded;
        public static AsyncOperationHandle<LocalizationSettings> InitializationOperation => ThrowInitialization
            ? throw new InvalidOperationException("Initialization unavailable")
            : new AsyncOperationHandle<LocalizationSettings> { Status = InitializationStatus };
        public static Locales AvailableLocales = new();
        public static StringDatabase StringDatabase = new();
        public static Locale SelectedLocale
        {
            get => throw new Exception("Must not depend on selected game language");
            set => throw new Exception("Must not change selected game language");
        }
    }
}

namespace OverlayHUD
{
    public sealed partial class Plugin
    {
        private bool nativeMode;
        private readonly Endpoint levelEndpoint = new();
        internal readonly Log Logger = new();
        internal readonly List<string> Posts = new();
        internal readonly Queue<Task<string>> Responses = new();
        internal Plugin(bool native) { nativeMode = native; }
        internal IEnumerator Sync() => SyncMonsterLocalization();
        private sealed class Endpoint { public string Value = "http://127.0.0.1:8787/api/level"; }
        internal sealed class Log
        {
            internal readonly List<string> Warnings = new();
            internal void LogWarning(string value) => Warnings.Add(value);
            internal void LogInfo(string value) { }
        }
        private string BuildSiblingEndpoint(string url, string route) => new Uri(new Uri(url), route).ToString();
        private Task<string> QueueNetworkRequest(Func<string> request)
        {
            string result = request();
            return Responses.Count > 0 ? Responses.Dequeue() : Task.FromResult(result);
        }
        private string SendHttpPost(string url, string json)
        {
            if (nativeMode) throw new Exception("Native must not send HTTP");
            if (url != "http://127.0.0.1:8787/api/monster-localization") throw new Exception("Wrong endpoint");
            Posts.Add(json);
            return "OK";
        }
        private bool IsHttpSuccess(string value) => value == "OK";
    }
}
