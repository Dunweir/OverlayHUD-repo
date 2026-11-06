using System.Collections;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OverlayHUD;
using UnityEngine;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;
using UnityEngine.ResourceManagement.AsyncOperations;

int checks = 0;
void Check(bool condition, string message)
{
    checks++;
    if (!condition) throw new Exception(message);
}

// Captured from the installed game's Addressables HUD_en-US/HUD_ru-RU tables,
// shared keys in localization-assets-shared_assets_all.bundle (2026-11-06).
// Monster fixture is the existing tools/extract-monster-localization.py output.
var monsters = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, string>>>(
    File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "monster-localization.json")));
var upgrades = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, string>>>(
    File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "upgrade-names.json")));
Dictionary<string, string> Table(string language) => monsters.ToDictionary(e => e.Key, e => e.Value[language])
    .Concat(upgrades.Values.Select(e => new KeyValuePair<string, string>(e["key"], e[language])))
    .ToDictionary(e => e.Key, e => e.Value);

var names = new HudNameLocalization();
Check(names.MonsterName("Banger", "ru", true, false) == "Banger", "Pre-init monster fallback");
Check(names.MonsterName(null, "ru", true, false) == "", "Null name fallback");
Check(names.UpgradeName(null, "ru") == "", "Null upgrade fallback");
Check(names.UpgradeName("modUpgrade", "ru") == "modUpgrade", "Unknown upgrade fallback");
foreach (var entry in upgrades)
    Check(names.UpgradeName(entry.Key, "ru") == entry.Value["en"], "Pre-init official English fallback: " + entry.Key);
foreach (string language in new[] { "ru", "en" })
    Check(names.SetTable(language, Table(language)), "Table rejected: " + language);
foreach (string language in new[] { "ru", "en", "ru" })
{
    foreach (var entry in upgrades)
        Check(names.UpgradeName(entry.Key, language) == entry.Value[language], "Upgrade key/language: " + entry.Key);
    foreach (var entry in monsters)
    {
        string canonical = entry.Value["en"];
        Check(names.MonsterName(canonical, language, true, false) == entry.Value[language], "Monster key/language: " + entry.Key);
        Check(names.MonsterName(canonical, language, false, false) == null, "Unseen name leaked: " + canonical);
        Check(names.MonsterName(canonical, language, true, true) == null, "Unknown name leaked: " + canonical);
    }
}
Check(names.MonsterName("Gnomes", "ru", true, false) == "Гном", "Grouped Gnomes identity");
Check(names.MonsterName("Snatcher", "ru", true, false) == "Пони", "Legacy Snatcher identity");
Check(names.MonsterName("Snatcher", "ru", true, true) == null, "Unknown Snatcher must stay hidden");
Check(names.MonsterName("Modded Enemy", "ru", true, false) == "Modded Enemy", "Unknown key must not be guessed");
Check(names.UpgradeName("tumbleLaunch", "de") == "Рывок при кувырке", "Native's default language must remain ru");
Check(names.EnemyNames().Count == monsters.Count, "Desktop payload includes upgrade labels");
Check(names.EnemyNames().All(e => e.Value.Count == 2), "Desktop lost a language");
Check(names.UpgradeNames().Count == upgrades.Count, "Desktop upgrade export count");
foreach (var entry in upgrades)
    foreach (string language in new[] { "ru", "en" })
        Check(names.UpgradeNames()[entry.Value["key"]][language] == names.UpgradeName(entry.Key, language),
            "Desktop and Native upgrade labels differ: " + entry.Key);
names.UpgradeNames()["STATS.UPGRADE.LAUNCH"]["ru"] = "mutated snapshot";
Check(names.UpgradeName("tumbleLaunch", "ru") == "Рывок при кувырке", "Upgrade export exposed mutable cache");
names.EnemyNames()["ENEMY.BANG"]["ru"] = "mutated snapshot";
Check(names.MonsterName("Banger", "ru", true, false) == "Взрывник", "Export must not expose mutable cache");

// English display changes must not remap stable canonical monster identities.
var english = Table("en");
english["ENEMY.BANG"] = "New official English name";
names.SetTable("en", english);
Check(names.MonsterName("Banger", "en", true, false) == "New official English name", "Canonical identity depends on translated text");
var russian = Table("ru");
russian["ENEMY.BANG"] = " ";
russian["STATS.UPGRADE.LAUNCH"] = null;
russian["STATS.UPGRADE.EXTRA_JUMP"] = "STATS.UPGRADE.EXTRA_JUMP";
russian["ITEM.UPGRADE_PLAYER_HEALTH"] = "Not a stat label";
names.SetTable("ru", russian);
Check(names.MonsterName("Banger", "ru", true, false) == "New official English name", "Blank Russian entry must use English");
Check(names.UpgradeName("tumbleLaunch", "ru") == "Tumble Launch", "Null Russian upgrade fallback");
Check(names.UpgradeName("extraJump", "ru") == "Extra Jump", "Raw localization key leaked");
Check(!names.SetTable("de", Table("en")) && !names.SetTable("ru", null)
    && !names.SetTable("ru", new Dictionary<string, string> { ["OTHER"] = "ignored" }), "Invalid table accepted");
Check(names.UpgradeName("health", "ru") == "Здоровье", "Invalid update discarded good cache");
names.Clear();
names.SetTable("ru", Table("ru"));
Check(names.MonsterName("Banger", "en", true, false) == "Banger", "English must not fall back to Russian");
Check(names.UpgradeName("extraJump", "en") == "Extra Jump", "English upgrade used Russian");

void ResetRuntime()
{
    LocalizationSettings.InitializationStatus = AsyncOperationStatus.Succeeded;
    LocalizationSettings.ThrowInitialization = false;
    LocalizationSettings.AvailableLocales = new Locales();
    LocalizationSettings.StringDatabase = new StringDatabase();
    foreach (string language in new[] { "en", "ru" })
        LocalizationSettings.StringDatabase.Tables[language == "en" ? "en-US" : "ru-RU"] =
            new StringTable { Values = Table(language) };
}
List<object> Drain(IEnumerator coroutine)
{
    var yields = new List<object>();
    while (coroutine.MoveNext())
    {
        if (yields.Count > 100) throw new Exception("Unbounded loading/retry loop");
        yields.Add(coroutine.Current);
    }
    return yields;
}

ResetRuntime();
var native = new Plugin(true);
var sync = native.Sync();
Check(sync.MoveNext() && sync.Current is AsyncOperationHandle<LocalizationSettings>, "Must await initialization");
Check(sync.MoveNext() && sync.Current is AsyncOperationHandle<StringTable>, "Must await English table");
Check(sync.MoveNext() && sync.Current is AsyncOperationHandle<StringTable>, "Must await Russian table");
Check(Plugin.LocalizedHudNames.UpgradeName("extraJump", "ru") == "Extra Jump", "English not published before Russian finishes");
Check(!sync.MoveNext(), "Native must finish after loading, without desktop polling");
Check(Plugin.LocalizedHudNames.UpgradeName("extraJump", "ru") == "Ещё один прыжок", "Coroutine did not populate Native cache");
Check(native.Posts.Count == 0 && LocalizationSettings.StringDatabase.Requests.SequenceEqual(new[] { "en-US", "ru-RU" }),
    "Native did HTTP or loaded tables repeatedly");

foreach (string failure in new[] { "missing-locale", "failed-operation", "null-table", "empty-table", "exception" })
{
    ResetRuntime();
    switch (failure)
    {
        case "missing-locale": LocalizationSettings.AvailableLocales.Missing.Add("ru-RU"); break;
        case "failed-operation": LocalizationSettings.StringDatabase.Failures["ru-RU"] = 10; break;
        case "null-table": LocalizationSettings.StringDatabase.Tables.Remove("ru-RU"); break;
        case "empty-table": LocalizationSettings.StringDatabase.Tables["ru-RU"].Values = Array.Empty<KeyValuePair<string, string>>(); break;
        case "exception": LocalizationSettings.StringDatabase.Throw.Add("ru-RU"); break;
    }
    native = new Plugin(true);
    var yields = Drain(native.Sync());
    Check(native.Posts.Count == 0, "Native failure sent HTTP: " + failure);
    Check(yields.OfType<WaitForSecondsRealtime>().Count() == 5, "Retry not bounded: " + failure);
    Check(LocalizationSettings.StringDatabase.Requests.Count(c => c == "en-US") == 1, "Reloaded successful locale: " + failure);
    Check(Plugin.LocalizedHudNames.UpgradeName("extraJump", "ru") == "Extra Jump", "Partial locale fallback: " + failure);
    Check(native.Logger.Warnings.Count > 0, "Failure not logged: " + failure);
}
ResetRuntime();
LocalizationSettings.StringDatabase.Failures["ru-RU"] = 1;
native = new Plugin(true);
Check(Drain(native.Sync()).OfType<WaitForSecondsRealtime>().Count() == 1, "Transient failure did not retry");
Check(Plugin.LocalizedHudNames.MonsterName("Gnomes", "ru", true, false) == "Гном", "Retry failed to populate cache");
ResetRuntime();
LocalizationSettings.AvailableLocales.Missing.Add("en-US");
native = new Plugin(true);
Drain(native.Sync());
Check(Plugin.LocalizedHudNames.UpgradeName("extraJump", "ru") == "Ещё один прыжок"
    && Plugin.LocalizedHudNames.UpgradeName("extraJump", "en") == "Extra Jump", "Missing English blocked Russian or changed English fallback");

foreach (bool throws in new[] { false, true })
{
    ResetRuntime();
    LocalizationSettings.InitializationStatus = AsyncOperationStatus.Failed;
    LocalizationSettings.ThrowInitialization = throws;
    native = new Plugin(true);
    Drain(native.Sync());
    Check(native.Logger.Warnings.Count == 1 && native.Posts.Count == 0
        && LocalizationSettings.StringDatabase.Requests.Count == 0, "Initialization failure is not safe");
    Check(Plugin.LocalizedHudNames.MonsterName("Banger", "ru", true, false) == "Banger", "Stale cache survived reinitialization");
}

ResetRuntime();
var desktop = new Plugin(false);
desktop.Responses.Enqueue(Task.FromException<string>(new Exception("Offline")));
desktop.Responses.Enqueue(Task.FromResult("HTTP failure"));
desktop.Responses.Enqueue(Task.FromCanceled<string>(new CancellationToken(true)));
sync = desktop.Sync();
var delays = new List<float>();
for (int step = 0; step < 30 && delays.Count < 5 && sync.MoveNext(); step++)
    if (sync.Current is WaitForSecondsRealtime wait) delays.Add(wait.Seconds);
Check(delays.SequenceEqual(new[] { 5f, 5f, 5f, 60f, 60f }), "Desktop startup/restart retry regressed");
Check(desktop.Posts.Count == 5, "Desktop did not republish names");
var payload = JObject.Parse(desktop.Posts[0]);
Check(payload["names"].Children().Count() == monsters.Count && (string)payload["names"]["ENEMY.BANG"]["ru"] == "Взрывник",
    "Desktop names payload changed or contains upgrades");
Check(payload["upgradeNames"].Children().Count() == upgrades.Count, "Desktop omitted upgrade names");
foreach (var entry in upgrades)
    foreach (string language in new[] { "ru", "en" })
        Check((string)payload["upgradeNames"][entry.Value["key"]][language]
            == Plugin.LocalizedHudNames.UpgradeName(entry.Key, language), "Plugin payload differs from Native: " + entry.Key);

ResetRuntime();
LocalizationSettings.AvailableLocales.Missing.Add("ru-RU");
desktop = new Plugin(false);
sync = desktop.Sync();
for (int step = 0; step < 30 && desktop.Posts.Count == 0 && sync.MoveNext(); step++) { }
Check(desktop.Posts.Count == 1 && JObject.Parse(desktop.Posts[0])["names"]["ENEMY.BANG"]["ru"] == null,
    "Missing Russian prevented partial desktop sync");
Check((string)JObject.Parse(desktop.Posts[0])["upgradeNames"]["STATS.UPGRADE.LAUNCH"]["en"] == "Tumble Launch"
    && JObject.Parse(desktop.Posts[0])["upgradeNames"]["STATS.UPGRADE.LAUNCH"]["ru"] == null,
    "Partial upgrade tables not exported");
ResetRuntime();
foreach (string locale in new[] { "en-US", "ru-RU" })
    LocalizationSettings.StringDatabase.Tables[locale].Values = new Dictionary<string, string>
    { ["STATS.UPGRADE.LAUNCH"] = "Updated official stat label" };
desktop = new Plugin(false);
sync = desktop.Sync();
for (int step = 0; step < 10 && desktop.Posts.Count == 0 && sync.MoveNext(); step++) { }
Check(desktop.Posts.Count == 1 && !JObject.Parse(desktop.Posts[0])["names"].HasValues
    && (string)JObject.Parse(desktop.Posts[0])["upgradeNames"]["STATS.UPGRADE.LAUNCH"]["ru"] == "Updated official stat label",
    "Upgrade-only game tables must publish even without enemy names");
ResetRuntime();
LocalizationSettings.AvailableLocales.Missing.UnionWith(new[] { "en-US", "ru-RU" });
desktop = new Plugin(false);
Drain(desktop.Sync());
Check(desktop.Posts.Count == 0, "Empty localization should not start HTTP polling");

// Integration contract: lookup happens after the existing snapshot/visibility gate;
// no roster lookup or state mutation is added to the tooltip path.
string tooltips = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "NativeHud.Tooltips.source"));
int gate = tooltips.IndexOf("if (!tile.Seen || monster == null || !monster.Seen || monster.Unknown", StringComparison.Ordinal);
int lookup = tooltips.IndexOf("Plugin.LocalizedHudNames.MonsterName(", StringComparison.Ordinal);
Check(gate >= 0 && lookup > gate && tooltips.Substring(gate, lookup - gate).Contains("continue;"), "Tooltip bypasses seen/unknown gate");
Check(tooltips.Contains("tile.Seen && monster.Seen, monster.Unknown") && !tooltips.Contains("state.Monsters"), "Tooltip uses hidden roster instead of displayed snapshot");
Check(tooltips.Contains("Plugin.LocalizedHudNames.UpgradeName(key, options.Language)"), "Upgrade tooltip not wired to localization cache");
string plugin = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Plugin.source"));
Check(plugin.Contains("            StartCoroutine(SyncMonsterLocalization());")
    && !plugin.Contains("if (!nativeMode) StartCoroutine(SyncMonsterLocalization());"), "Native localization not started");
Console.WriteLine($"Localization checks passed: {checks} (30 monsters, 12 upgrades, ru/en, fallback, visibility, coroutine and desktop retries).");
