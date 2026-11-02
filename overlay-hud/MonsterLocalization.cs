using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Localization.Settings;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace OverlayHUD
{
    public sealed partial class Plugin
    {
        private IEnumerator SyncMonsterLocalization()
        {
            // Explicit locales: never change the game's selected language.
            var initialization = LocalizationSettings.InitializationOperation;
            yield return initialization;
            if (initialization.Status != AsyncOperationStatus.Succeeded)
            {
                Logger.LogWarning("Game localization initialization failed; using bundled enemy names.");
                yield break;
            }

            string json = null;
            while (json == null)
            {
                var names = new Dictionary<string, Dictionary<string, string>>();
                bool complete = true;
                foreach (string code in new[] { "en-US", "ru-RU" })
                {
                    var locale = LocalizationSettings.AvailableLocales.GetLocale(code);
                    if (locale == null) { complete = false; continue; }
                    var operation = LocalizationSettings.StringDatabase.GetTableAsync("HUD", locale);
                    yield return operation;
                    if (operation.Status != AsyncOperationStatus.Succeeded || operation.Result == null)
                    {
                        complete = false;
                        continue;
                    }
                    // The database owns and caches these table handles.
                    foreach (var entry in operation.Result.Values)
                    {
                        if (entry.Key == null || !entry.Key.StartsWith("ENEMY.", StringComparison.Ordinal)
                            || string.IsNullOrWhiteSpace(entry.Value)) continue;
                        if (!names.TryGetValue(entry.Key, out var translations))
                        {
                            translations = new Dictionary<string, string>();
                            names.Add(entry.Key, translations);
                        }
                        translations[code.Substring(0, 2)] = entry.Value;
                    }
                }
                if (complete && names.Count > 0)
                {
                    var builder = new StringBuilder("{\"names\":{");
                    bool first = true;
                    foreach (var entry in names)
                    {
                        if (!first) builder.Append(',');
                        first = false;
                        builder.Append('"').Append(EscapeJson(entry.Key)).Append("\":{");
                        bool firstLanguage = true;
                        foreach (var translation in entry.Value)
                        {
                            if (!firstLanguage) builder.Append(',');
                            firstLanguage = false;
                            builder.Append('"').Append(translation.Key).Append("\":\"")
                                .Append(EscapeJson(translation.Value)).Append('"');
                        }
                        builder.Append('}');
                    }
                    json = builder.Append("}}").ToString();
                    Logger.LogInfo("Loaded " + names.Count + " official enemy names from the game's ru/en HUD tables.");
                }
                else yield return new WaitForSecondsRealtime(10f);
            }

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
    }
}
