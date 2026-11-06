using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace OverlayHUD
{
    public sealed partial class Plugin
    {
        internal static readonly HudNameLocalization LocalizedHudNames = new HudNameLocalization();

        private IEnumerator SyncMonsterLocalization()
        {
            LocalizedHudNames.Clear();
            // Explicit locales: never change the game's selected language or mutate a
            // LocalizedAsset's shared LocaleOverride. No synchronous Addressables waits.
            AsyncOperationHandle<LocalizationSettings> initialization;
            try { initialization = LocalizationSettings.InitializationOperation; }
            catch (Exception ex)
            {
                Logger.LogWarning("Game localization unavailable; using fallback HUD names. " + ex.Message);
                yield break;
            }
            yield return initialization;
            if (initialization.Status != AsyncOperationStatus.Succeeded)
            {
                Logger.LogWarning("Game localization initialization failed; using fallback HUD names.");
                yield break;
            }

            var loaded = new HashSet<string>();
            // Missing tables/locales must not block the other language or retry forever.
            for (int attempt = 0; attempt < 6 && loaded.Count < 2; attempt++)
            {
                foreach (string code in new[] { "en-US", "ru-RU" })
                {
                    if (loaded.Contains(code) || !TryRequestHudTable(code, out var operation)) continue;
                    yield return operation;
                    if (operation.Status != AsyncOperationStatus.Succeeded || operation.Result == null) continue;
                    // The database owns and caches these table handles; do not release them.
                    var entries = new List<KeyValuePair<string, string>>();
                    foreach (var entry in operation.Result.Values)
                        entries.Add(new KeyValuePair<string, string>(entry.Key, entry.Value));
                    if (LocalizedHudNames.SetTable(code.Substring(0, 2), entries)) loaded.Add(code);
                }
                if (loaded.Count < 2 && attempt < 5) yield return new WaitForSecondsRealtime(10f);
            }
            Logger.LogInfo("Loaded official HUD names (ENEMY.* / STATS.UPGRADE.*) for "
                + loaded.Count + "/2 ru/en locales.");
            if (loaded.Count < 2)
                Logger.LogWarning("Some HUD localization tables are unavailable; missing names use English/original fallbacks.");

            // Native uses the cache directly. It must never start a desktop HTTP loop.
            if (nativeMode) yield break;
            var names = LocalizedHudNames.EnemyNames();
            if (names.Count == 0) yield break;
            string json = JsonConvert.SerializeObject(new { names });

            // Retry startup races and republish after a desktop-server restart.
            while (true)
            {
                string url = BuildSiblingEndpoint(levelEndpoint.Value, "/api/monster-localization");
                Task<string> request = QueueNetworkRequest(() => SendHttpPost(url, json));
                while (!request.IsCompleted) yield return null;
                bool success = !request.IsFaulted && !request.IsCanceled && IsHttpSuccess(request.Result);
                yield return new WaitForSecondsRealtime(success ? 60f : 5f);
            }
        }

        private bool TryRequestHudTable(string code, out AsyncOperationHandle<StringTable> operation)
        {
            operation = default;
            try
            {
                var locale = LocalizationSettings.AvailableLocales.GetLocale(code);
                if (locale == null) return false;
                operation = LocalizationSettings.StringDatabase.GetTableAsync("HUD", locale);
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogWarning("Cannot load HUD localization for " + code + ": " + ex.Message);
                return false;
            }
        }
    }
}
