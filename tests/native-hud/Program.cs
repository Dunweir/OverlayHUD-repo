using OverlayHUD;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
static void Destroyed(NativeHudState target, string player, string item, double now)
{
    target.Apply("item-destroyed", Newtonsoft.Json.JsonConvert.SerializeObject(new { player, item }), now);
}

var defaults = new NativeHudOptions();
Check(defaults.Scale == 1 && defaults.OffsetX == 0 && defaults.OffsetY == 20
    && defaults.MonsterSize == 70 && defaults.UpgradeSize == 38 && defaults.Columns == 4
    && defaults.HorizontalColumns == 11 && defaults.UpgradeRows == 2
    && defaults.Language == "ru" && defaults.Alignment == "Center" && defaults.HiddenUpgrades.Count == 0,
    "Native HUD layout/language defaults changed");
Check(defaults.HorizontalLayout && defaults.Enabled && defaults.ShowLevel && defaults.ShowTimer
    && defaults.ShowMapValue && defaults.ShowLostValue && defaults.ShowMonsters && defaults.ShowProximityWaves
    && defaults.ShowRespawnIndicator && defaults.ShowDestroyedItemAlerts && defaults.ShowHealth
    && defaults.ShowRespawnTimer && defaults.ShowStrength && defaults.ShowUpgrades && defaults.ShowMonsterCounts
    && defaults.ShowDamageNumbers && defaults.Animations && defaults.WrapValueLabels
    && !defaults.OnlyAliveMonsters && !defaults.FilterUndetectedMonsters && !defaults.ShowTooltips,
    "Native HUD toggle defaults changed");
Check(defaults.Translate("Уровень", "Level") == "Уровень", "Russian translation failed");
defaults.Language = "en";
Check(defaults.Translate("Уровень", "Level") == "Level", "English translation failed");
new NativeHudOptions().HiddenUpgrades.Add("strength");
Check(defaults.HiddenUpgrades.Count == 0, "Options shared mutable upgrade defaults");
// Each row is ShowMonsters/OnlyAlive/FilterUndetected; columns are
// unseen-dead, unseen-alive, seen-dead, seen-alive. FilterUndetected refines OnlyAlive.
bool[][] monsterTruthTable =
{
    new[] { false, false, false, false }, new[] { false, false, false, false },
    new[] { false, false, false, false }, new[] { false, false, false, false },
    new[] { true, true, true, true }, new[] { true, true, true, true },
    new[] { true, true, false, true }, new[] { false, true, false, true }
};
for (int row = 0; row < 8; row++)
for (int column = 0; column < 4; column++)
{
    var options = new NativeHudOptions
    {
        ShowMonsters = (row & 4) != 0, OnlyAliveMonsters = (row & 2) != 0,
        FilterUndetectedMonsters = (row & 1) != 0
    };
    var monster = new NativeHudState.Monster { Seen = (column & 2) != 0, Alive = (column & 1) != 0 };
    Check(options.ShouldDisplayMonster(monster) == monsterTruthTable[row][column],
        $"Monster option truth table failed at row {row}, column {column}");
}
Console.WriteLine("Native HUD option checks passed (32 visibility combinations).");

static void TimestampLevel(NativeTimestampWriter writer, NativeHudState target, string json,
    double now, bool save = true, string language = "ru")
{
    // Match Plugin.Native: sample direct visibility/config changes before dispatch, then finalize before Apply.
    writer.Tick(now, save);
    writer.BeforeLevel(json, now, save, language);
    target.Apply("level", json, now);
}

string timestampRoot = Path.Combine(Path.GetTempPath(), "OverlayHUD-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(timestampRoot);
try
{
    string directory = Path.Combine(timestampRoot, "timestamps");
    var timestampState = new NativeHudState();
    var errors = new List<Exception>();
    DateTime clock = new DateTime(2026, 11, 4, 9, 8, 7, 123, DateTimeKind.Local);
    using var writer = new NativeTimestampWriter(timestampState, directory, errors.Add, () => clock);
    writer.Tick(0, true);
    foreach (string invalid in new[] { "{}", "{\"level\":0}", "{\"level\":-1}", "{\"level\":null}",
        "{\"level\":\"NaN\"}", "{\"level\":\"Infinity\"}", "{\"level\":2147483648}", "{\"level\":true}", "{\"level\":{}}" })
        TimestampLevel(writer, timestampState, invalid, 0);
    Check(!Directory.Exists(directory) && errors.Count == 0, "Writer touched disk/logs before a valid level");
    TimestampLevel(writer, timestampState, "{\"level\":1}", 0, false);
    writer.Tick(1, true);
    Check(!Directory.Exists(directory), "Enabling started recording mid-level");

    // Reserve the exact name beforehand: even a millisecond collision must never overwrite another session.
    Directory.CreateDirectory(directory);
    string occupied = Path.Combine(directory, "2026-11-04_09-08-07-123.txt");
    File.WriteAllText(occupied, "existing session");
    TimestampLevel(writer, timestampState, "{\"level\":1,\"levelName\":\"Level - Усадьба\"}", 10);
    await writer.FlushAsync();
    string file = Path.Combine(directory, "2026-11-04_09-08-07-123_1.txt");
    string firstHeading = "09:08:07 Уровень 1 Level - Усадьба";
    Check(File.ReadAllText(file) == firstHeading + Environment.NewLine, "Initial localized timestamp/map incorrect");
    Roster(timestampState, "Trudge", new[] { 1, 2 });
    Roster(timestampState, "Secret Unseen", new[] { 3 });
    Roster(timestampState, "Gnome", new[] { 10, 11, 12, 13, 14, 15, 16, 17 });
    timestampState.Apply("monster-seen", "{\"id\":1}", 11);
    timestampState.Apply("monster-seen", "{\"id\":2}", 11);
    timestampState.Apply("monster-seen", "{\"id\":11}", 11);
    timestampState.Apply("monster-seen", "{\"id\":20,\"name\":\"Модовый зверь\",\"unknown\":true}", 11);
    timestampState.Apply("monster-status", "{\"statuses\":[{\"id\":1,\"alive\":false,\"health\":0},{\"id\":3,\"health\":999}]}", 12);
    timestampState.Apply("tab-hidden", "{\"hidden\":true}", 12);
    Task unchanged = writer.FlushAsync();
    for (int frame = 0; frame < 100; frame++) writer.Tick(12 + frame, true);
    Check(ReferenceEquals(unchanged, writer.FlushAsync()) && File.ReadAllText(file) == firstHeading + Environment.NewLine,
        "Hidden HUD finalized the level or ticks queued disk writes");
    TimestampLevel(writer, timestampState, "{\"level\":0}", 13, language: "en");
    Check(ReferenceEquals(unchanged, writer.FlushAsync()), "Invalid level finalized the active timestamp");

    clock = clock.AddMinutes(2);
    TimestampLevel(writer, timestampState, "{\"level\":2.9,\"levelName\":\"Museum\"}", 130, language: "en");
    await writer.FlushAsync();
    string finalizedFirst = firstHeading + " [Gnomes x4, Trudge x2, Модовый зверь]";
    string secondHeading = "09:10:07 Level 2 Museum";
    Check(File.ReadAllLines(file).SequenceEqual(new[] { finalizedFirst, secondHeading }),
        "Transition lost old monsters, leaked an unseen group/species, or changed frozen language");
    Check(timestampState.Monsters.Count == 0, "Timestamp transition did not apply the new level");
    byte[] bytes = File.ReadAllBytes(file);
    Check(new System.Text.UTF8Encoding(false, true).GetString(bytes) == File.ReadAllText(file), "Timestamps are not valid UTF8");
    Check(!bytes.Take(3).SequenceEqual(new byte[] { 0xef, 0xbb, 0xbf }), "Unexpected UTF8 BOM");

    timestampState.Apply("monster-seen", "{\"id\":30,\"name\":\"Banger\"}", 131);
    timestampState.Visible = false; // The real PostVisibility native path bypasses dispatch.
    writer.Tick(132, true);
    await writer.FlushAsync();
    Check(File.ReadAllLines(file).Last() == secondHeading + " [Banger]", "Direct visibility false did not finalize");
    unchanged = writer.FlushAsync();
    writer.Tick(133, true);
    writer.End(133);
    timestampState.Visible = true;
    writer.Tick(134, true);
    Check(ReferenceEquals(unchanged, writer.FlushAsync()), "End/visibility repeats wrote again or resumed the level");

    // Same-number level routes still start a fresh line; disable finalizes once and enable waits for a route.
    TimestampLevel(writer, timestampState, "{\"level\":2,\"levelName\":\"Arena\"}", 140, language: "en");
    timestampState.Apply("monster-seen", "{\"id\":40,\"name\":\"Chef\"}", 141);
    writer.Tick(142, false);
    await writer.FlushAsync();
    Check(File.ReadAllLines(file).Last() == "09:10:07 Level 2 Arena [Chef]", "Disabling lost the active monster list");
    unchanged = writer.FlushAsync();
    writer.Tick(143, false);
    writer.Tick(144, true);
    timestampState.Apply("monster-seen", "{\"id\":41,\"name\":\"Late monster\"}", 145);
    writer.End(146);
    Check(ReferenceEquals(unchanged, writer.FlushAsync()), "Disable/end were not idempotent or enabling resumed mid-level");
    TimestampLevel(writer, timestampState, "{\"level\":3}", 150);
    timestampState.Apply("monster-seen", "{\"id\":50,\"name\":\"Final monster\"}", 151);
    writer.Dispose();
    await writer.FlushAsync();
    Check(File.ReadAllLines(file).SequenceEqual(new[] { finalizedFirst, secondHeading + " [Banger]",
        "09:10:07 Level 2 Arena [Chef]", "09:10:07 Уровень 3 ? [Final monster]" }),
        "Disposal or re-enabling leaked previous monsters or failed to finalize");
    unchanged = writer.FlushAsync();
    writer.Dispose();
    writer.Tick(152, false);
    writer.BeforeLevel("{\"level\":4}", 153, true, "en");
    Check(ReferenceEquals(unchanged, writer.FlushAsync()), "Disposed writer continued recording");

    clock = clock.AddMinutes(-2);
    var otherState = new NativeHudState();
    using var other = new NativeTimestampWriter(otherState, directory, errors.Add, () => clock);
    TimestampLevel(other, otherState, "{\"level\":1}", 0);
    other.Dispose();
    await other.FlushAsync();
    Check(File.Exists(Path.Combine(directory, "2026-11-04_09-08-07-123_2.txt"))
        && File.ReadAllText(occupied) == "existing session" && File.ReadAllLines(file).Length == 4,
        "A second session overwrote an existing file");
    Check(errors.Count == 0, "Successful timestamp sessions logged errors");

    string blocked = Path.Combine(timestampRoot, "blocked");
    File.WriteAllText(blocked, "not a directory");
    var failedState = new NativeHudState();
    using var failedWriter = new NativeTimestampWriter(failedState, blocked, errors.Add, () => clock);
    TimestampLevel(failedWriter, failedState, "{\"level\":1}", 0);
    await failedWriter.FlushAsync();
    Check(errors.Count == 1, "IO failure was not logged exactly once");
    File.Delete(blocked);
    unchanged = failedWriter.FlushAsync();
    for (int level = 2; level < 10; level++)
    {
        TimestampLevel(failedWriter, failedState, "{\"level\":" + level + "}", level);
        failedWriter.Tick(level, false);
    }
    failedWriter.Dispose();
    await failedWriter.FlushAsync();
    Check(errors.Count == 1 && !Directory.Exists(blocked) && ReferenceEquals(unchanged, failedWriter.FlushAsync()),
        "Failed writer retried disk access, logged repeatedly or queued more writes");
    Check(failedState.Level == 9, "Timestamp IO failure blocked state dispatch");

    string writeFailureDirectory = Path.Combine(timestampRoot, "write-failure");
    var writeFailureState = new NativeHudState();
    var writeErrors = new List<Exception>();
    using var writeFailure = new NativeTimestampWriter(writeFailureState, writeFailureDirectory, writeErrors.Add, () => clock);
    TimestampLevel(writeFailure, writeFailureState, "{\"level\":1}", 0);
    await writeFailure.FlushAsync();
    string writeFailureFile = Directory.GetFiles(writeFailureDirectory).Single();
    File.Delete(writeFailureFile);
    Directory.CreateDirectory(writeFailureFile); // Force failure on an already established session path.
    writeFailure.Tick(1, false);
    await writeFailure.FlushAsync();
    Directory.Delete(writeFailureFile);
    TimestampLevel(writeFailure, writeFailureState, "{\"level\":2}", 2);
    writeFailure.Dispose();
    await writeFailure.FlushAsync();
    Check(writeErrors.Count == 1 && Directory.GetFileSystemEntries(writeFailureDirectory).Length == 0,
        "A later write failure retried, logged repeatedly or allocated another filename");

    using var unused = new NativeTimestampWriter(new NativeHudState(), Path.Combine(timestampRoot, "unused"), errors.Add);
    unused.Dispose();
    await unused.FlushAsync();
    Check(!Directory.Exists(Path.Combine(timestampRoot, "unused")) && errors.Count == 1,
        "Disposal without a level created a file or logged an error");
}
finally
{
    Directory.Delete(timestampRoot, true);
}
Console.WriteLine("Native HUD timestamp checks passed.");

var alerts = new NativeHudState();
alerts.Apply("level", "{\"level\":1}", 0);
Destroyed(alerts, "  Ив\u0000ан\t  Smith\n", "  Ваза\r\n золотая\u200b ", 10);
var firstAlert = alerts.LastDestroyedItem;
Check(firstAlert.Player == "Иван Smith" && firstAlert.Item == "Ваза золотая" && firstAlert.At == 10,
    "Alert names were not sanitized or native seconds were lost");
var alertClock = new NativeAlertLifetime();
alertClock.Update(firstAlert, true, 10);
Check(alertClock.Current == firstAlert && alertClock.ShownAt == 10, "Fresh alert did not start");
Destroyed(alerts, "Bob", "Cup", 10);
var secondAlert = alerts.LastDestroyedItem;
Check(secondAlert.Sequence > firstAlert.Sequence && secondAlert.At == firstAlert.At,
    "Same-timestamp alert did not get a new sequence");
alertClock.Update(secondAlert, true, 11);
Check(alertClock.Current == secondAlert && alertClock.ShownAt == 11, "Newest same-time alert did not replace current");
alertClock.Update(secondAlert, true, 16.999);
Check(alertClock.Current == secondAlert && alertClock.ShownAt == 11, "Repeated ticks restarted or prematurely expired alert");
alertClock.Update(secondAlert, true, 17);
Check(alertClock.Current == null, "Alert outlived six seconds");
alertClock.Update(secondAlert, true, 18);
Check(alertClock.Current == null, "Consumed alert replayed");
foreach (string badName in new[] { "null", "42", "true", "{}", "[]", "\"\"", "\" \\t\\r\\n\\u0000\\u200b\"" })
{
    alerts.Apply("item-destroyed", "{\"player\":" + badName + ",\"item\":\"Cup\"}", 19);
    alerts.Apply("item-destroyed", "{\"player\":\"Bob\",\"item\":" + badName + "}", 19);
    Check(alerts.LastDestroyedItem == secondAlert, "Invalid name replaced the latest alert: " + badName);
}
alerts.Apply("item-destroyed", "{}", 19);
Check(alerts.LastDestroyedItem == secondAlert, "Missing names replaced the latest alert");
foreach (double invalidTime in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
    Destroyed(alerts, "Bob", "Cup", invalidTime);
Check(alerts.LastDestroyedItem == secondAlert, "Non-finite timestamp replaced the latest alert");
Destroyed(alerts, new string('Я', 70), new string('A', 90), 20);
Check(alerts.LastDestroyedItem.Player == new string('Я', 60) && alerts.LastDestroyedItem.Item == new string('A', 80),
    "Player/item caps are not 60/80 characters");
Check(alerts.LastDestroyedItem.Sequence == secondAlert.Sequence + 1, "Invalid payloads advanced sequence");
Destroyed(alerts, new string('A', 59) + "😀", new string('B', 79) + "😀", 20);
Check(alerts.LastDestroyedItem.Player.Length == 59 && alerts.LastDestroyedItem.Item.Length == 79,
    "Name caps split a surrogate pair");

var freshClock = new NativeAlertLifetime();
freshClock.Update(alerts.LastDestroyedItem, true, 35);
Check(freshClock.Current != null, "Exactly fifteen-second-old event was rejected");
freshClock.Update(alerts.LastDestroyedItem, true, 40.999);
Check(freshClock.Current != null, "Freshness limit incorrectly shortened the six-second presentation");
freshClock.Update(alerts.LastDestroyedItem, true, 41);
Check(freshClock.Current == null, "Delayed fresh event outlived its presentation");
var staleClock = new NativeAlertLifetime();
staleClock.Update(alerts.LastDestroyedItem, true, 35.001);
Check(staleClock.Current == null, "Stale event was shown");
staleClock.Update(alerts.LastDestroyedItem, true, 35);
Check(staleClock.Current == null, "Stale event was not consumed");

Destroyed(alerts, "Bob", "Plate", 50);
alertClock.Update(alerts.LastDestroyedItem, true, 50);
alertClock.Update(alerts.LastDestroyedItem, false, 51);
Check(alertClock.Current == null, "Disabling did not immediately hide the active alert");
alertClock.Update(alerts.LastDestroyedItem, true, 51);
Check(alertClock.Current == null, "Re-enabling replayed the active alert");
Destroyed(alerts, "Bob", "Bottle", 52);
alertClock.Update(alerts.LastDestroyedItem, false, 52);
alertClock.Update(alerts.LastDestroyedItem, true, 53);
Check(alertClock.Current == null, "Re-enabling replayed an alert received while disabled");
Destroyed(alerts, "Bob", "Statue", 54);
alertClock.Update(alerts.LastDestroyedItem, true, 54);
Destroyed(alerts, "Bob", "Vase", 55);
alertClock.Update(alerts.LastDestroyedItem, false, 55);
alertClock.Update(alerts.LastDestroyedItem, true, 55);
Check(alertClock.Current == null, "Disabling failed to consume both the active and pending replacement");

alerts.Apply("tab-hidden", "{\"hidden\":true}", 60);
Destroyed(alerts, "Bob", "Hidden item", 60);
alertClock.Update(alerts.LastDestroyedItem, true, 60);
Check(alertClock.Current != null && alertClock.ShownAt == 60, "Hidden HUD did not consume a fresh event");
alertClock.Update(alerts.LastDestroyedItem, true, 66);
alerts.Apply("tab-hidden", "{\"hidden\":false}", 67);
alertClock.Update(alerts.LastDestroyedItem, true, 67);
Check(alertClock.Current == null, "Hidden HUD paused an alert or replayed it on reveal");
alerts.Visible = false;
Destroyed(alerts, "Bob", "Inactive item", 70);
alertClock.Update(alerts.LastDestroyedItem, true, 70);
alertClock.Update(alerts.LastDestroyedItem, true, 76);
alerts.Visible = true;
alertClock.Update(alerts.LastDestroyedItem, true, 77);
Check(alertClock.Current == null, "Inactive HUD replayed an expired alert");
Destroyed(alerts, "Bob", "Old level item", 80);
alertClock.Update(alerts.LastDestroyedItem, true, 80);
long oldSequence = alerts.LastDestroyedItem.Sequence;
alerts.Apply("level", "{\"level\":1}", 80);
Check(alerts.LastDestroyedItem == null, "Same-number level transition retained the alert");
alertClock.Update(alerts.LastDestroyedItem, true, 80);
Check(alertClock.Current == null, "Level transition retained the active presentation");
Destroyed(alerts, "Bob", "New level item", 80);
Check(alerts.LastDestroyedItem.Sequence > oldSequence, "Level transition reset the monotonic sequence");
alertClock.Update(alerts.LastDestroyedItem, true, 80);
Check(alertClock.Current == alerts.LastDestroyedItem, "New level's same-timestamp event was lost");
alerts.Apply("level", "{\"level\":2}", 81);
Destroyed(alerts, "Bob", "Next item", 81);
alertClock.Update(alerts.LastDestroyedItem, true, 81);
Check(alertClock.Current == alerts.LastDestroyedItem && alertClock.ShownAt == 81,
    "Level and new event between ticks retained the previous presentation");
Destroyed(alerts, "Bob", "Pending old item", 82);
alerts.Apply("level", "{\"level\":3}", 82);
alertClock.Update(alerts.LastDestroyedItem, true, 82);
Check(alertClock.Current == null && alerts.LastDestroyedItem == null, "Level transition retained a pending alert");
Console.WriteLine("Native HUD destroyed-item alert checks passed.");

// Native artwork must contain-fit, never stretch portrait upgrades into squares.
float portraitScale = NativeHudLayout.ContainScale(128, 256, 26);
Check(portraitScale * 128 == 13 && portraitScale * 256 == 26, "Portrait upgrade was squashed");
float landscapeScale = NativeHudLayout.ContainScale(256, 128, 26);
Check(landscapeScale * 256 == 26 && landscapeScale * 128 == 13, "Landscape upgrade was stretched");
foreach (float scale in new[] { .2f, .5f, 1f })
{
    float baseline = NativeHudLayout.TextBaseline(-18, 9, 64, scale);
    float top = baseline - 9 * scale;
    float bottom = baseline - 64 * scale;
    Check(Math.Abs((top + bottom) / 2 + 18) < .0001f, "Font metrics did not center the line in its pill");
}
var state = new NativeHudState();
state.Apply("level", "{\"level\":1}", 10);
state.Apply("roster", "{\"monsters\":[{\"id\":7,\"name\":\"Trudge\"}]}", 11);
state.Apply("monster-status", "{\"statuses\":[{\"id\":7,\"health\":80,\"maxHealth\":100,\"alive\":true}]}", 12);
Check(!state.Monsters[7].Seen, "Roster/status exposed an unseen enemy");
state.Apply("roster", "{\"monsters\":[{\"id\":7,\"tier\":3}]}", 12);
Check(state.Monsters[7].Tier == 3 && !state.Monsters[7].Seen, "Tier metadata revealed an enemy");
state.Apply("monster-seen", "{\"id\":7,\"name\":\"Trudge\"}", 13);
state.Apply("monster-status", "{\"statuses\":[{\"id\":7,\"alive\":false,\"respawnRemaining\":30}]}", 15);
Check(state.Monsters[7].Seen && state.Monsters[7].Health == 80, "Partial timer update erased reveal/health");
Check(state.Monsters[7].RespawnAt == 45, "Respawn deadline incorrect");
Check(state.Monsters[7].Tier == 3, "Partial timer update erased tier metadata");
state.Apply("monster-status", "{\"statuses\":[{\"id\":7,\"health\":0}]}", 16);
Check(state.Monsters[7].Health == 0, "Zero health was discarded");
state.Apply("tab-hidden", "{\"hidden\":true}", 17);
Check(state.Hidden && state.StartedAt == 10, "Hiding UI reset the timer");
state.Apply("visibility", "{\"visible\":false}", 18);
Check(!state.Visible, "Transition did not hide HUD");
state.Apply("tab-hidden", "{\"hidden\":false}", 19);
state.Apply("upgrades", "{\"upgrades\":{\"strength\":4,\"health\":8}}", 19);
state.Apply("upgrades", "{\"upgrades\":{\"strength\":5}}", 19);
Check(state.Upgrades["strength"] == 5 && state.Upgrades["health"] == 8, "Partial upgrade update erased other upgrades");
state.Apply("upgrades", "{\"upgrades\":{\"strength\":0}}", 19);
Check(state.Upgrades["strength"] == 0, "Upgrade reset was ignored");
state.Apply("map-value", "{\"value\":12345,\"initial\":13000,\"lost\":655,\"goal\":5000}", 19);
Check(state.MapValue == 12345 && state.LostValue == 655, "Map value confused current/initial/goal values");
state.Apply("map-value", "{\"value\":0,\"lost\":0}", 19);
Check(state.MapValue == 0 && state.LostValue == 0, "Map value reset was ignored");
state.Apply("map-value", "{\"value\":1000,\"lost\":100}", 19);
state.Apply("level", "{\"level\":2}", 20);
Check(state.Monsters.Count == 0 && state.Visible && state.StartedAt == 20, "New level retained old enemies/timer");
Check(state.Upgrades.Count == 0 && state.MapValue == 0 && state.LostValue == 0, "New level retained stale upgrades/map value");
// Visibility (including Plugin.PostVisibility's direct assignment) never pauses the timer.
state.Apply("tab-hidden", "{\"hidden\":true}", 21);
state.Visible = false;
state.Apply("visibility", "{\"visible\":true}", 25);
Check(state.StartedAt == 20 && state.Hidden, "Visibility changed the level timer or tab state");
state.Apply("level", "{\"level\":2}", 30);
Check(state.StartedAt == 30 && state.Hidden && state.Visible, "Same-number level did not restart timer or preserved visibility incorrectly");

var respawn = new NativeHudState();
respawn.Apply("monster-seen", "{\"id\":1,\"name\":\"Trudge\"}", 0);
Check(respawn.Monsters[1].RespawnDuration == 0 && respawn.Monsters[1].CooldownReducedAt == -10,
    "New monster invented cooldown visuals");
respawn.Apply("monster-status", "{\"statuses\":[{\"id\":1,\"alive\":false,\"respawnRemaining\":30}]}", 10);
Check(respawn.Monsters[1].RespawnDuration == 30 && respawn.Monsters[1].CooldownReducedAt == -10,
    "First death did not initialize duration without a reduction flash");
respawn.Apply("monster-status", "{\"statuses\":[{\"id\":1,\"alive\":false,\"respawnRemaining\":29.5}]}", 11);
Check(respawn.Monsters[1].RespawnAt == 40 && respawn.Monsters[1].RespawnDuration == 30
    && respawn.Monsters[1].CooldownReducedAt == -10, "Small respawn correction changed cooldown visuals");
respawn.Apply("monster-status", "{\"statuses\":[{\"id\":1,\"alive\":false,\"respawnRemaining\":10}]}", 12);
Check(respawn.Monsters[1].RespawnAt == 22 && respawn.Monsters[1].RespawnDuration == 30
    && respawn.Monsters[1].CooldownReducedAt == 12, "Significant respawn reduction was ignored or shrank duration");
var cooldownCopy = respawn.GetDisplayMonsters(12).Single();
Check(cooldownCopy.RespawnDuration == 30 && cooldownCopy.CooldownReducedAt == 12,
    "Singleton display lost cooldown visuals");
cooldownCopy.RespawnDuration = 999;
cooldownCopy.CooldownReducedAt = 999;
Check(respawn.Monsters[1].RespawnDuration == 30 && respawn.Monsters[1].CooldownReducedAt == 12,
    "Display cooldown mutation changed raw state");
Check(respawn.GetDisplayMonsters(22).Single().RespawnDuration == 0
    && respawn.GetDisplayMonsters(22).Single().CooldownReducedAt == -10,
    "Expired singleton countdown retained visual metadata");
respawn.Apply("roster", "{\"monsters\":[{\"id\":1,\"tier\":3}]}", 13);
respawn.Apply("monster-seen", "{\"id\":1,\"name\":\"Trudge\"}", 13);
respawn.Apply("monster-status", "{\"statuses\":[{\"id\":1,\"health\":0}]}", 13);
Check(respawn.Monsters[1].RespawnAt == 22 && !respawn.Monsters[1].Alive
    && respawn.Monsters[1].RespawnDuration == 30 && respawn.Monsters[1].CooldownReducedAt == 12,
    "Partial metadata changed respawn visuals");
respawn.Apply("monster-status", "{\"statuses\":[{\"id\":1,\"alive\":true}]}", 14);
Check(respawn.Monsters[1].RespawnAt == 0 && respawn.Monsters[1].RespawnDuration == 0
    && respawn.Monsters[1].CooldownReducedAt == -10, "Revival retained stale cooldown visuals");
respawn.Apply("monster-status", "{\"statuses\":[{\"id\":1,\"alive\":true,\"respawnRemaining\":20}]}", 15);
Check(respawn.Monsters[1].RespawnAt == 0, "Alive enemy acquired a respawn deadline");
respawn.Apply("monster-status", "{\"statuses\":[{\"id\":1,\"alive\":false,\"respawnRemaining\":30}]}", 16);
Check(respawn.Monsters[1].RespawnAt == 46 && respawn.Monsters[1].RespawnDuration == 30
    && respawn.Monsters[1].CooldownReducedAt == -10, "New death reused old cooldown visuals");
respawn.Apply("monster-status", "{\"statuses\":[{\"id\":1,\"alive\":false,\"respawnRemaining\":30.5}]}", 17);
Check(respawn.Monsters[1].RespawnAt == 47.5 && respawn.Monsters[1].RespawnDuration == 30.5
    && respawn.Monsters[1].CooldownReducedAt == -10, "Upward boundary correction lost duration or flashed");
respawn.Apply("monster-status", "{\"statuses\":[{\"id\":1,\"alive\":false,\"respawnRemaining\":28}]}", 18);
Check(respawn.Monsters[1].RespawnAt == 46 && respawn.Monsters[1].CooldownReducedAt == -10,
    "Exact 1.5-second reduction did not correct deadline or incorrectly flashed");
respawn.Apply("monster-status", "{\"statuses\":[{\"id\":1,\"alive\":false,\"respawnRemaining\":24}]}", 19);
Check(respawn.Monsters[1].RespawnAt == 43 && respawn.Monsters[1].RespawnDuration == 30.5
    && respawn.Monsters[1].CooldownReducedAt == 19, "Reduction beyond jitter did not record timestamp");
foreach (string remaining in new[] { "0", "-1", "null", "\"NaN\"", "\"Infinity\"", "\"-Infinity\"" })
{
    respawn.Apply("monster-status", "{\"statuses\":[{\"id\":1,\"alive\":false,\"respawnRemaining\":30}]}", 20);
    respawn.Apply("monster-status", "{\"statuses\":[{\"id\":1,\"alive\":false,\"respawnRemaining\":" + remaining + "}]}", 21);
    Check(respawn.Monsters[1].RespawnAt == 0 && respawn.Monsters[1].RespawnDuration == 0
        && respawn.Monsters[1].CooldownReducedAt == -10, "Invalid/zero remaining retained cooldown visuals: " + remaining);
}
respawn.Apply("monster-status", "{\"statuses\":[{\"id\":1,\"alive\":false,\"respawnRemaining\":30}]}", 22);
respawn.Apply("monster-status", "{\"statuses\":[{\"id\":1,\"alive\":false}]}", 23);
Check(respawn.Monsters[1].RespawnAt == 0 && !respawn.Monsters[1].Alive
    && respawn.Monsters[1].RespawnDuration == 0 && respawn.Monsters[1].CooldownReducedAt == -10,
    "Missing countdown retained visual metadata or revived enemy");
respawn.Apply("monster-status", "{\"statuses\":[{\"id\":1,\"respawnRemaining\":10}]}", 24);
respawn.Apply("monster-status", "{\"statuses\":[{\"id\":1,\"respawnRemaining\":10.5}]}", 24);
Check(respawn.Monsters[1].RespawnAt == 34 && respawn.Monsters[1].RespawnDuration == 10.5
    && respawn.Monsters[1].CooldownReducedAt == -10, "Duration did not track maximum during ignored deadline jitter");
respawn.Apply("monster-status", "{\"statuses\":[{\"id\":1,\"respawnRemaining\":7.5}]}", 25);
Check(respawn.Monsters[1].CooldownReducedAt == -10, "Boundary reduction unexpectedly flashed");
respawn.Apply("monster-status", "{\"statuses\":[{\"id\":1,\"respawnRemaining\":5.9}]}", 25);
Check(respawn.Monsters[1].CooldownReducedAt == 25, "Reduction between jitter and web flash threshold was ignored");
respawn.Apply("monster-status", "{\"statuses\":[{\"id\":1,\"respawnRemaining\":4.9}]}", 26);
Check(respawn.Monsters[1].CooldownReducedAt == 25, "Natural countdown retriggered reduction feedback");
respawn.Apply("monster-status", "{\"statuses\":[{\"id\":1,\"respawnRemaining\":40}]}", 27);
Check(respawn.Monsters[1].RespawnAt == 67 && respawn.Monsters[1].RespawnDuration == 40
    && respawn.Monsters[1].CooldownReducedAt == 25, "Upward correction failed to grow duration or retriggered feedback");

var health = new NativeHudState();
health.Apply("monster-status", "{\"statuses\":[{\"id\":3,\"alive\":true,\"health\":80,\"maxHealth\":100}]}", 1);
foreach (string value in new[] { "null", "\"NaN\"", "\"Infinity\"", "\"-Infinity\"" })
{
    health.Apply("monster-status", "{\"statuses\":[{\"id\":3,\"alive\":true,\"health\":" + value + ",\"maxHealth\":" + value + "}]}", 2);
    Check(health.Monsters[3].Health == 80 && health.Monsters[3].MaxHealth == 100, "Invalid health replaced known values: " + value);
}
health.Apply("monster-status", "{\"statuses\":[{\"id\":3,\"alive\":true,\"health\":-10,\"maxHealth\":-20}]}", 3);
Check(health.Monsters[3].Health == 0 && health.Monsters[3].MaxHealth == 0, "Negative health was not clamped");
Check(!health.Monsters[3].Seen, "Health update revealed an unseen enemy");

state.Apply("roster", "{\"monsters\":[{\"id\":7}]}", 31);
state.Apply("upgrades", "{\"strength\":5}", 31);
state.Apply("map-value", "{\"value\":100,\"lost\":10}", 31);
foreach (string level in new[] { "0", "-1", "null", "\"NaN\"", "\"Infinity\"" })
{
    state.Apply("level", "{\"level\":" + level + "}", 32);
    Check(state.Level == 2 && state.StartedAt == 30 && state.Monsters.Count == 1
        && state.Upgrades["strength"] == 5 && state.MapValue == 100 && state.LostValue == 10,
        "Invalid level reset state: " + level);
}
state.Apply("level", "{}", 33);
Check(state.Level == 2 && state.StartedAt == 30 && state.Monsters.Count == 1, "Missing level reset state");
state.Apply("level", "{\"level\":3.9}", 40);
Check(state.Level == 3 && state.StartedAt == 40 && state.Monsters.Count == 0, "Level was not floored/reset");
var originalCulture = System.Globalization.CultureInfo.CurrentCulture;
try
{
    foreach (string culture in new[] { "ru-RU", "en-US" })
    {
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo(culture);
        health.Apply("monster-status", "{\"statuses\":[{\"id\":3,\"alive\":true,\"health\":12.5,\"maxHealth\":100.5}]}", 4);
        Check(health.Monsters[3].Health == 12.5f && health.Monsters[3].MaxHealth == 100.5f,
            "Fractional health depended on culture: " + culture);
    }
}
finally
{
    System.Globalization.CultureInfo.CurrentCulture = originalCulture;
}
Console.WriteLine("Native HUD state checks passed.");

static void Roster(NativeHudState target, string name, IEnumerable<int> ids, int? tier = 1, bool unknown = false)
{
    target.Apply("roster", Newtonsoft.Json.JsonConvert.SerializeObject(new
    {
        monsters = ids.Select(id => new { id, name, tier, unknown }).ToArray()
    }), 0);
}

var grouped = new NativeHudState();
Roster(grouped, "Gnome", new[] { 40, 30, 20, 10 });
var tiles = grouped.GetDisplayMonsters(0);
Check(tiles.Count == 1 && tiles[0].Id == 40 && tiles[0].Count == 4 && tiles[0].Name == "Gnomes",
    "Four gnomes did not produce one canonical tile with the first arrival ID");
Check(!tiles[0].Seen && tiles[0].Health == null && tiles[0].MaxHealth == null,
    "Unseen group revealed itself or invented health");
Check(grouped.Monsters.Count == 4 && grouped.Monsters.Values.All(m => m.Count == 1 && !m.Seen),
    "Aggregation changed raw instances");
grouped.Apply("monster-seen", "{\"id\":20}", 1);
Check(grouped.GetDisplayMonsters(1).Single().Seen && !grouped.Monsters[40].Seen,
    "Seeing a non-leading member did not reveal the group independently of raw state");
Roster(grouped, "gNo-MeS", new[] { 5, 6, 7, 8 });
tiles = grouped.GetDisplayMonsters(1);
Check(tiles.Count == 2 && tiles[0].Id == 5 && tiles[1].Id == 40 && tiles.All(m => m.Count == 4),
    "Eight gnomes did not produce two stable ID-sorted groups");
Check(!tiles[0].Seen && tiles[1].Seen, "Reveal leaked across groups of the same species");
Roster(grouped, "Gnomes", new[] { 8, 7, 6, 5, 10, 20, 30, 40, 40 });
Check(grouped.Monsters.Count == 8 && grouped.GetDisplayMonsters(2).Select(m => (m.Id, m.Count, m.Seen))
    .SequenceEqual(tiles.Select(m => (m.Id, m.Count, m.Seen))), "Repeated/reordered roster shuffled or duplicated groups");
tiles[1].Health = 999;
tiles[1].Name = "Changed";
tiles[1].Count = 99;
Check(grouped.GetDisplayMonsters(2)[1].Name == "Gnomes" && grouped.GetDisplayMonsters(2)[1].Health == null
    && grouped.Monsters[40].Health == null, "Returned group was not a fresh copy");

foreach (var rule in new[] { ("Banger", 3), ("Bangers", 6), ("Animals", 3), ("Upscreams", 3),
    ("Bowties", 3), ("Mentalists", 3), ("Peepers", 3), ("Chefs", 3), ("Spewers", 4) })
{
    var sample = new NativeHudState();
    Roster(sample, rule.Item1, Enumerable.Range(1, rule.Item2 + 1), 3);
    var groups = sample.GetDisplayMonsters(0);
    Check(groups.Count == 2 && groups[0].Count == rule.Item2 && groups[1].Count == 1,
        "Server grouping capacity mismatch: " + rule.Item1);
}
foreach (string name in new[] { "Trudge", "Animal", "Upscream", "Bowtie", "Mentalist", "Peeper", "Chef Frog",
    "Rugrat", "Hidden", "Bella", "Birthday Boy", "Elsa", "Headgrab", "Tick", "Gambit", "Heart Hugger", "Oogly",
    "Apex Predators", "Shadow Children", "Custom Gnomes" })
{
    var sample = new NativeHudState();
    Roster(sample, name, new[] { 1, 2 }, 3);
    Check(sample.GetDisplayMonsters(0).Count == 2, "Grouped individual/unrecognized server species: " + name);
    var singleton = sample.GetDisplayMonsters(0)[0];
    singleton.Seen = true;
    singleton.Health = 123;
    Check(!sample.Monsters[1].Seen && sample.Monsters[1].Health == null
        && !sample.GetDisplayMonsters(0)[0].Seen, "Singleton was not copied");
}
var tiers = new NativeHudState();
Roster(tiers, "Gnomes", new[] { 30, 31 }, 3);
Roster(tiers, "Gnomes", new[] { 20, 21 }, 2);
Roster(tiers, "Gnomes", new[] { 10, 11 }, 1);
Roster(tiers, "Gnomes", new[] { 1, 2 }, null);
Check(tiers.GetDisplayMonsters(0).Select(m => m.Id).SequenceEqual(new[] { 10, 20, 30, 1 })
    && tiers.GetDisplayMonsters(0).All(m => m.Count == 2), "Spawn tiers mixed or sorted incorrectly");

var unknowns = new NativeHudState();
Roster(unknowns, "Gnomes", new[] { 1, 2 }, 1, true);
Roster(unknowns, "Gnomes", new[] { 3, 4 });
unknowns.Apply("monster-seen", "{\"id\":1,\"name\":\"Gnomes\"}", 1);
Check(unknowns.GetDisplayMonsters(1).Count == 3 && unknowns.GetDisplayMonsters(1)[0].Count == 1,
    "Unknown metadata was ignored or erased by a seen event");
unknowns.Apply("monster-status", "{\"statuses\":[{\"id\":3,\"unknown\":true}]}", 1);
Check(unknowns.GetDisplayMonsters(1).Count == 4, "Late unknown metadata did not disable grouping");

var incremental = new NativeHudState();
incremental.Apply("monster-status", "{\"statuses\":[{\"id\":99,\"health\":5}]}", 0);
incremental.Apply("monster-seen", "{\"id\":8,\"name\":\"Gnome\",\"tier\":1}", 0);
Check(incremental.GetDisplayMonsters(0).Single(m => m.Id == 8).Count == 1, "Partial group invented full membership");
Roster(incremental, "Gnomes", new[] { 7, 6 });
Check(incremental.GetDisplayMonsters(0).Single(m => m.Id == 8).Count == 3, "Incremental arrivals changed group ID");
Roster(incremental, "Gnomes", new[] { 5, 4, 3, 2, 1 });
Roster(incremental, "Gnomes", new[] { 99 });
Check(incremental.GetDisplayMonsters(0).Select(m => (m.Id, m.Count)).SequenceEqual(new[] { (4, 4), (8, 4), (99, 1) }),
    "Late name metadata for an early status shuffled existing full groups");
Roster(incremental, "Gnomes", new[] { 1, 2, 3, 4, 5, 6, 7, 8, 99 });
Check(incremental.GetDisplayMonsters(0).Count == 3 && incremental.Monsters.Count == 9,
    "Events before repeated roster produced duplicate membership");

var mixed = new NativeHudState();
Roster(mixed, "Gnomes", Enumerable.Range(1, 4));
mixed.Apply("monster-status", "{\"statuses\":[{\"id\":1,\"alive\":false,\"health\":0,\"maxHealth\":10,\"respawnRemaining\":5},{\"id\":2,\"alive\":false,\"health\":2,\"maxHealth\":20,\"respawnRemaining\":30},{\"id\":3,\"alive\":true,\"health\":7},{\"id\":4,\"alive\":false,\"maxHealth\":40,\"respawnRemaining\":20}]}", 10);
var aggregate = mixed.GetDisplayMonsters(16).Single();
Check(aggregate.Alive && aggregate.RespawnAt == 0 && aggregate.Health == 9 && aggregate.MaxHealth == 70
    && !aggregate.Seen, "Mixed alive group did not sum independently known HP or suppress respawn/reveal");
mixed.Apply("monster-status", "{\"statuses\":[{\"id\":3,\"alive\":false,\"health\":0}]}", 16);
aggregate = mixed.GetDisplayMonsters(16).Single();
Check(!aggregate.Alive && aggregate.RespawnAt == 30 && aggregate.Health == 2 && aggregate.MaxHealth == 70,
    "All-dead group did not select earliest future deadline or sum cached health");
Check(mixed.GetDisplayMonsters(30).Single().RespawnAt == 40 && mixed.GetDisplayMonsters(40).Single().RespawnAt == 0,
    "Expired deadlines remained visible");
Check(mixed.Monsters[1].RespawnAt == 15 && mixed.Monsters[2].Health == 2, "Display aggregation mutated raw timer/health");
mixed.Apply("monster-status", "{\"statuses\":[{\"id\":4,\"alive\":true}]}", 20);
Check(mixed.GetDisplayMonsters(20).Single().RespawnAt == 0 && mixed.GetDisplayMonsters(20).Single().Alive,
    "One revived member failed to clear group countdown");
incremental.Apply("level", "{\"level\":2}", 50);
Check(incremental.GetDisplayMonsters(50).Count == 0, "Level reset retained display groups");
Roster(incremental, "Gnomes", new[] { 1, 8, 99 });
Check(incremental.GetDisplayMonsters(50).Single().Id == 1 && incremental.GetDisplayMonsters(50).Single().Count == 3
    && !incremental.GetDisplayMonsters(50).Single().Seen, "Level reset retained grouping history/reveal");
Console.WriteLine("Native HUD grouping checks passed.");

var proximity = new NativeHudState();
proximity.Apply("monster-status", "{\"statuses\":[{\"id\":1,\"alive\":true,\"playerClose\":true,\"playerVeryClose\":true}]}", 0);
Check(proximity.Monsters[1].PlayerClose && proximity.Monsters[1].PlayerVeryClose && !proximity.Monsters[1].Seen,
    "Incoming proximity was lost or revealed an unseen singleton");
var proximityTile = proximity.GetDisplayMonsters(0).Single();
Check(!proximityTile.Seen && !proximityTile.PlayerClose && !proximityTile.PlayerVeryClose,
    "Unseen singleton exposed proximity visuals");
proximity.Apply("monster-seen", "{\"id\":1,\"name\":\"Trudge\"}", 1);
proximity.Apply("roster", "{\"monsters\":[{\"id\":1,\"tier\":3}]}", 1);
proximity.Apply("monster-status", "{\"statuses\":[{\"id\":1,\"alive\":true,\"health\":50}]}", 1);
proximityTile = proximity.GetDisplayMonsters(1).Single();
Check(proximityTile.PlayerClose && proximityTile.PlayerVeryClose, "Partial events erased cached proximity");
proximityTile.PlayerClose = proximityTile.PlayerVeryClose = false;
Check(proximity.Monsters[1].PlayerClose && proximity.Monsters[1].PlayerVeryClose,
    "Singleton display proximity mutation changed raw state");
foreach (string invalid in new[] { "null", "1", "\"true\"", "[]", "{}" })
{
    proximity.Apply("monster-status", "{\"statuses\":[{\"id\":1,\"playerClose\":" + invalid
        + ",\"playerVeryClose\":" + invalid + "}]}", 2);
    Check(proximity.Monsters[1].PlayerClose && proximity.Monsters[1].PlayerVeryClose,
        "Non-boolean proximity replaced known values: " + invalid);
}
proximity.Apply("monster-status", "{\"statuses\":[{\"id\":1,\"playerClose\":false}]}", 2);
Check(!proximity.Monsters[1].PlayerClose && proximity.GetDisplayMonsters(2).Single().PlayerVeryClose,
    "Independent proximity flags were conflated or explicit false was ignored");
proximity.Apply("monster-status", "{\"statuses\":[{\"id\":1,\"playerVeryClose\":false}]}", 2);
Check(!proximity.GetDisplayMonsters(2).Single().PlayerVeryClose, "Explicit very-close false was ignored");
proximity.Apply("monster-status", "{\"statuses\":[{\"id\":1,\"alive\":false,\"respawnRemaining\":30,\"playerClose\":true,\"playerVeryClose\":true}]}", 3);
proximity.Apply("monster-status", "{\"statuses\":[{\"id\":1,\"playerClose\":true,\"playerVeryClose\":true}]}", 4);
Check(!proximity.Monsters[1].PlayerClose && !proximity.Monsters[1].PlayerVeryClose,
    "Dead enemy retained proximity from death or partial events");
proximity.Apply("monster-status", "{\"statuses\":[{\"id\":1,\"alive\":true}]}", 5);
Check(!proximity.GetDisplayMonsters(5).Single().PlayerClose && !proximity.GetDisplayMonsters(5).Single().PlayerVeryClose,
    "Revival restored stale proximity");

var visualGroup = new NativeHudState();
Roster(visualGroup, "Gnomes", new[] { 1, 2, 3 });
visualGroup.Apply("monster-status", "{\"statuses\":[{\"id\":2,\"alive\":true,\"playerClose\":true,\"playerVeryClose\":true}]}", 0);
Check(!visualGroup.GetDisplayMonsters(0).Single().Seen && !visualGroup.GetDisplayMonsters(0).Single().PlayerClose
    && !visualGroup.GetDisplayMonsters(0).Single().PlayerVeryClose, "Unseen group exposed proximity");
visualGroup.Apply("monster-seen", "{\"id\":1}", 1);
Check(!visualGroup.GetDisplayMonsters(1).Single().PlayerClose && !visualGroup.GetDisplayMonsters(1).Single().PlayerVeryClose,
    "Seen group leaked an unseen member's proximity");
visualGroup.Apply("monster-seen", "{\"id\":2}", 2);
var visualTile = visualGroup.GetDisplayMonsters(2).Single();
Check(visualTile.PlayerClose && visualTile.PlayerVeryClose, "Non-leading seen member did not contribute proximity");
visualTile.PlayerClose = visualTile.PlayerVeryClose = false;
Check(visualGroup.Monsters[2].PlayerClose && visualGroup.GetDisplayMonsters(2).Single().PlayerVeryClose,
    "Group display proximity mutation changed cached flags");
visualGroup.Apply("monster-status", "{\"statuses\":[{\"id\":2,\"alive\":false,\"respawnRemaining\":40}]}", 10);
Check(!visualGroup.GetDisplayMonsters(10).Single().PlayerClose && !visualGroup.GetDisplayMonsters(10).Single().PlayerVeryClose,
    "Dead seen member contributed proximity to an alive group");
visualGroup.Apply("monster-status", "{\"statuses\":[{\"id\":2,\"alive\":false,\"respawnRemaining\":20}]}", 11);
visualTile = visualGroup.GetDisplayMonsters(11).Single();
Check(visualTile.Alive && visualTile.RespawnDuration == 0 && visualTile.CooldownReducedAt == -10,
    "Alive group exposed a dead member's cooldown visuals");
visualGroup.Apply("monster-status", "{\"statuses\":[{\"id\":1,\"alive\":false,\"respawnRemaining\":60},{\"id\":3,\"alive\":false,\"respawnRemaining\":80}]}", 12);
visualGroup.Apply("monster-status", "{\"statuses\":[{\"id\":1,\"alive\":false,\"respawnRemaining\":40}]}", 13);
visualGroup.Apply("monster-status", "{\"statuses\":[{\"id\":3,\"alive\":false,\"respawnRemaining\":50}]}", 14);
visualTile = visualGroup.GetDisplayMonsters(14).Single();
Check(!visualTile.Alive && visualTile.RespawnAt == 31 && visualTile.RespawnDuration == 40
    && visualTile.CooldownReducedAt == 13,
    "Group lost earliest member's duration/latest seen reduction or leaked unseen reduction");
Check(visualGroup.Monsters[3].CooldownReducedAt == 14 && !visualGroup.Monsters[3].Seen,
    "Unseen cooldown status was not cached privately");
visualTile.RespawnDuration = visualTile.CooldownReducedAt = 999;
Check(visualGroup.GetDisplayMonsters(14).Single().RespawnDuration == 40
    && visualGroup.GetDisplayMonsters(14).Single().CooldownReducedAt == 13,
    "Group display cooldown mutation changed cached metadata");
visualGroup.Apply("monster-seen", "{\"id\":3}", 15);
Check(visualGroup.GetDisplayMonsters(15).Single().CooldownReducedAt == 14,
    "Latest suitable reduction from another member was not selected");
visualTile = visualGroup.GetDisplayMonsters(31).Single();
Check(visualTile.RespawnAt == 53 && visualTile.RespawnDuration == 60,
    "Duration did not follow earliest unexpired member");
visualTile = visualGroup.GetDisplayMonsters(64).Single();
Check(visualTile.RespawnAt == 0 && visualTile.RespawnDuration == 0 && visualTile.CooldownReducedAt == -10,
    "Expired group countdown retained visual metadata");
visualGroup.Apply("monster-status", "{\"statuses\":[{\"id\":3,\"alive\":true,\"playerVeryClose\":true}]}", 16);
visualTile = visualGroup.GetDisplayMonsters(16).Single();
Check(visualTile.Alive && visualTile.PlayerVeryClose && !visualTile.PlayerClose
    && visualTile.RespawnDuration == 0 && visualTile.CooldownReducedAt == -10,
    "Revived group did not suppress cooldown visuals or retain independent proximity");
visualGroup.Apply("level", "{\"level\":2}", 20);
Roster(visualGroup, "Gnomes", new[] { 1, 2, 3 });
Check(visualGroup.Monsters.Values.All(m => !m.Seen && !m.PlayerClose && !m.PlayerVeryClose
    && m.RespawnDuration == 0 && m.CooldownReducedAt == -10), "Level reset retained visual metadata");

var privateCooldown = new NativeHudState();
privateCooldown.Apply("monster-status", "{\"statuses\":[{\"id\":1,\"alive\":false,\"respawnRemaining\":30}]}", 0);
privateCooldown.Apply("monster-status", "{\"statuses\":[{\"id\":1,\"alive\":false,\"respawnRemaining\":10}]}", 1);
Check(!privateCooldown.GetDisplayMonsters(1).Single().Seen
    && privateCooldown.GetDisplayMonsters(1).Single().CooldownReducedAt == -10,
    "Unseen singleton exposed reduction feedback");
Roster(privateCooldown, "Gnomes", new[] { 1 });
Check(!privateCooldown.GetDisplayMonsters(1).Single().Seen
    && privateCooldown.GetDisplayMonsters(1).Single().CooldownReducedAt == -10,
    "Unseen group exposed reduction feedback");
privateCooldown.Apply("monster-seen", "{\"id\":1}", 2);
Check(privateCooldown.GetDisplayMonsters(2).Single().RespawnDuration == 30
    && privateCooldown.GetDisplayMonsters(2).Single().CooldownReducedAt == 1,
    "Reveal erased cached cooldown metadata");
Console.WriteLine("Native HUD visual state checks passed.");

static void StrengthEvent(NativeHudState target, string route, string fields, int id = 1)
{
    string entry = "{\"id\":" + id + ",\"alive\":true" + fields + "}";
    target.Apply(route, route == "monster-seen" ? entry
        : "{\"" + (route == "roster" ? "monsters" : "statuses") + "\":[" + entry + "]}", 0);
}

// Expected values checked against server.js normalizeDynamicStrength, including
// float32 boundaries and counts beyond Int32.MaxValue / near MAX_SAFE_INTEGER.
var strengthCases = new (string Value, string Maximum, double? Expected)[]
{
    ("0", "6", 0), ("-0.0", "6", 0), ("6", "6", 0),
    ("6.0000001", "6", 0), ("6.0000003", "6", 1),
    ("7.2", "6", 1), ("7.2000003", "6", 1), ("7.200001", "6", 2),
    ("8.4", "6", 2), ("12", "6", 5), ("12", "4", 10),
    ("6", "6.0000001", 0), ("1e-46", "6", 0),
    ("1", "1e-46", null), ("1", "1.401298464324817e-45", null),
    ("3.4028234663852886e38", "6", null), ("1e39", "6", null), ("6", "1e39", null),
    ("4294967296", "1", 21474836474d),
    ("1801439716730470", "1", 9007198717870074d),
    ("1801439850948198", "1", null), ("1801439985165926", "1", null)
};
var strengthState = new NativeHudState();
foreach (var test in strengthCases)
{
    StrengthEvent(strengthState, "roster", ",\"strength\":" + test.Value + ",\"strengthForceMax\":" + test.Maximum);
    Check(strengthState.Monsters[1].RequiredStrength == test.Expected && !strengthState.Monsters[1].StrengthImpossible,
        $"Strength boundary {test.Value}/{test.Maximum}: expected {test.Expected}, got {strengthState.Monsters[1].RequiredStrength}");
    Check(!strengthState.Monsters[1].Seen && !strengthState.GetDisplayMonsters(0).Single().Seen,
        "Strength metadata revealed an unseen singleton");
}
foreach (string route in new[] { "roster", "monster-status", "monster-seen" })
{
    foreach (string invalid in new[] { "null", "true", "false", "\"12\"", "\"\"", "\"NaN\"", "\"Infinity\"",
        "\"Impossible\"", "\" impossible\"", "[]", "{}", "-1", "-1e-46", "NaN", "Infinity", "-Infinity" })
    {
        StrengthEvent(strengthState, route, ",\"strength\":12,\"strengthForceMax\":6");
        StrengthEvent(strengthState, route, ",\"strength\":" + invalid + ",\"strengthForceMax\":6");
        Check(strengthState.Monsters[1].RequiredStrength == null && !strengthState.Monsters[1].StrengthImpossible,
            "Explicit invalid strength did not clear cache: " + route + " " + invalid);
    }
    foreach (string invalid in new[] { "null", "true", "false", "\"6\"", "\"\"", "[]", "{}", "0", "-1", "NaN", "Infinity", "-Infinity" })
    {
        StrengthEvent(strengthState, route, ",\"strength\":\"impossible\"");
        StrengthEvent(strengthState, route, ",\"strength\":12,\"strengthForceMax\":" + invalid);
        Check(strengthState.Monsters[1].RequiredStrength == null && !strengthState.Monsters[1].StrengthImpossible,
            "Invalid force maximum did not clear impossibility: " + route + " " + invalid);
    }
    StrengthEvent(strengthState, route, ",\"strength\":12,\"strengthForceMax\":6");
    StrengthEvent(strengthState, route, "");
    StrengthEvent(strengthState, route, ",\"strengthForceMax\":4");
    Check(strengthState.Monsters[1].RequiredStrength == 5, "Missing strength changed cached count");
    StrengthEvent(strengthState, route, ",\"strength\":12");
    Check(strengthState.Monsters[1].RequiredStrength == null, "Missing force maximum reused stale metadata");
    StrengthEvent(strengthState, route, ",\"strength\":\"impossible\",\"strengthForceMax\":false");
    StrengthEvent(strengthState, route, "");
    Check(strengthState.Monsters[1].StrengthImpossible && strengthState.Monsters[1].RequiredStrength == null,
        "Impossible required a valid force maximum or was erased by a partial event");
    var copy = strengthState.GetDisplayMonsters(0).Single();
    Check(copy.StrengthImpossible && copy.RequiredStrength == null && !strengthState.CanLift(copy),
        "Singleton lost impossibility");
    StrengthEvent(strengthState, route, ",\"strength\":6,\"strengthForceMax\":6");
    copy = strengthState.GetDisplayMonsters(0).Single();
    Check(!copy.StrengthImpossible && copy.RequiredStrength == 0 && strengthState.CanLift(copy),
        "Valid observation did not replace impossibility or default upgrade count was not zero");
    copy.RequiredStrength = 99;
    copy.StrengthImpossible = true;
    Check(strengthState.Monsters[1].RequiredStrength == 0 && !strengthState.Monsters[1].StrengthImpossible,
        "Singleton strength copy mutated raw state");
}
var strengthGroup = new NativeHudState();
Roster(strengthGroup, "Gnomes", new[] { 1, 2, 3, 4 });
Check(strengthGroup.GetDisplayMonsters(0).Single().RequiredStrength == null
    && !strengthGroup.CanLift(strengthGroup.GetDisplayMonsters(0).Single()), "Unknown strength invented a requirement");
StrengthEvent(strengthGroup, "roster", ",\"strength\":12,\"strengthForceMax\":6", 1);
StrengthEvent(strengthGroup, "monster-status", ",\"strength\":18,\"strengthForceMax\":6", 2);
var strengthTile = strengthGroup.GetDisplayMonsters(0).Single();
Check(strengthTile.RequiredStrength == 10 && !strengthTile.Seen && strengthTile.Count == 4
    && strengthGroup.Monsters[1].RequiredStrength == 5 && strengthGroup.Monsters[3].RequiredStrength == null,
    "Group did not take maximum known strength independently of reveal/raw instances");
strengthGroup.Apply("upgrades", "{\"strength\":9}", 0);
Check(!strengthGroup.CanLift(strengthTile), "Insufficient upgrades allowed lifting");
strengthGroup.Apply("upgrades", "{\"strength\":10}", 0);
Check(strengthGroup.CanLift(strengthTile), "Exact required upgrades did not allow lifting");
StrengthEvent(strengthGroup, "monster-status", ",\"strength\":\"impossible\"", 3);
strengthTile = strengthGroup.GetDisplayMonsters(0).Single();
Check(strengthTile.StrengthImpossible && strengthTile.RequiredStrength == null && !strengthGroup.CanLift(strengthTile)
    && !strengthTile.Seen && strengthGroup.Monsters.Values.All(m => !m.Seen), "Impossible did not dominate or revealed group");
StrengthEvent(strengthGroup, "monster-status", ",\"strength\":null", 3);
StrengthEvent(strengthGroup, "monster-status", ",\"strength\":null", 2);
Check(strengthGroup.GetDisplayMonsters(0).Single().RequiredStrength == 5
    && !strengthGroup.GetDisplayMonsters(0).Single().StrengthImpossible, "Cleared members retained aggregate strength");
StrengthEvent(strengthGroup, "monster-seen", "", 4);
Check(strengthGroup.GetDisplayMonsters(0).Single().Seen && strengthGroup.GetDisplayMonsters(0).Single().RequiredStrength == 5,
    "Seen event erased group strength");
StrengthEvent(strengthGroup, "roster", ",\"strength\":\"impossible\"", 2);
strengthGroup.Apply("level", "{\"level\":2}", 1);
Check(strengthGroup.Monsters.Count == 0 && strengthGroup.GetDisplayMonsters(1).Count == 0, "Reset retained strength instances");
Roster(strengthGroup, "Gnomes", new[] { 1, 2, 3, 4 });
Check(strengthGroup.Monsters.Values.All(m => m.RequiredStrength == null && !m.StrengthImpossible && !m.Seen)
    && strengthGroup.GetDisplayMonsters(1).Single().RequiredStrength == null, "Reused IDs retained prior level strength");
Console.WriteLine("Native HUD strength checks passed.");

static void Near(float actual, float expected, string message)
{
    Check(Math.Abs(actual - expected) <= .001f, $"{message}: expected {expected}, got {actual}");
}

Near(NativeHudLayout.MonsterRowPitch(70, true), 102, "Health strip must reserve space");
Near(NativeHudLayout.MonsterRowPitch(70, false), 84, "Hidden health must release space");
Near(NativeHudLayout.MonsterRowPitch(35, false), 42, "Row spacing must follow configured monster size");
Check(NativeHudLayout.MonsterSize == 70, "Monster size changed");
Check(NativeHudLayout.ColumnPitch == 80, "Column pitch changed");
Check(NativeHudLayout.RowPitch == 102, "Row pitch changed");
Check(NativeHudLayout.BorderWidth == 3.28125f, "Border width changed");
foreach (var grid in new[] { (Cols: -1, Width: 0f), (Cols: 0, Width: 0f),
    (Cols: 1, Width: 70f), (Cols: 4, Width: 310f), (Cols: 11, Width: 870f), (Cols: 24, Width: 1910f) })
{
    Check(NativeHudLayout.GridWidth(grid.Cols) == grid.Width, "Grid width mismatch: " + grid.Cols);
    foreach (bool horizontal in new[] { false, true })
        Check(NativeHudLayout.Width(horizontal, grid.Cols) == grid.Width + 20,
            "Panel width must add only padding: " + grid.Cols);
}
Near(NativeHudLayout.Width(true, 11), 890, "Default horizontal width");
Near(NativeHudLayout.Width(true, 24), 1930, "24-column horizontal width");
Near(NativeHudLayout.Width(false, 24), 1930, "24-column grid width");
Near(NativeHudLayout.Width(false, 4), 330, "Four-column grid width");
Near(NativeHudLayout.Width(true, 1), 90, "Narrow strip must size its header dynamically");
Near(NativeHudLayout.Width(true, 12), 970, "12-column horizontal width");

foreach (var band in new[] { (Ratio: -1f, Band: 0), (Ratio: 0f, Band: 0),
    (Ratio: .1f, Band: 0), (Ratio: .3f, Band: 1), (Ratio: .6f, Band: 2),
    (Ratio: 1f, Band: 3), (Ratio: 2f, Band: 3) })
    Check(NativeHudLayout.HealthBand(band.Ratio) == band.Band, "Health band mismatch: " + band.Ratio);
foreach (var boundary in new[] { (Ratio: .1f, Band: 0), (Ratio: .3f, Band: 1), (Ratio: .6f, Band: 2) })
{
    Check(NativeHudLayout.HealthBand(MathF.BitDecrement(boundary.Ratio)) == boundary.Band,
        "Health band changed below boundary: " + boundary.Ratio);
    Check(NativeHudLayout.HealthBand(MathF.BitIncrement(boundary.Ratio)) == boundary.Band + 1,
        "Health band did not change above boundary: " + boundary.Ratio);
}

Check(NativeHudLayout.CooldownFraction(10, 40, 30) == 1, "Full cooldown fraction");
Check(NativeHudLayout.CooldownFraction(25, 40, 30) == .5f, "Half cooldown fraction");
Check(NativeHudLayout.CooldownFraction(40, 40, 30) == 0, "Deadline cooldown fraction");
Check(NativeHudLayout.CooldownFraction(41, 40, 30) == 0, "Expired cooldown must clamp to zero");
Check(NativeHudLayout.CooldownFraction(0, 40, 30) == 1, "Excess cooldown must clamp to one");
Check(NativeHudLayout.CooldownFraction(0, 1, double.Epsilon) == 1, "Tiny positive duration must clamp to one");
Check(NativeHudLayout.CooldownFraction(-double.MaxValue, double.MaxValue, 30) == 0,
    "Non-finite remaining time must return zero");
foreach (double duration in new[] { 0, -1, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
    Check(NativeHudLayout.CooldownFraction(10, 40, duration) == 0, "Invalid cooldown duration: " + duration);
foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
{
    Check(NativeHudLayout.CooldownFraction(invalid, 40, 30) == 0, "Invalid cooldown clock: " + invalid);
    Check(NativeHudLayout.CooldownFraction(10, invalid, 30) == 0, "Invalid cooldown deadline: " + invalid);
}

foreach (var curve in new[] { (X1: 0f, Y1: 0f, X2: 1f, Y2: 1f),
    (X1: .25f, Y1: .1f, X2: .25f, Y2: 1f), (X1: .42f, Y1: 0f, X2: .58f, Y2: 1f),
    (X1: 0f, Y1: 0f, X2: 0f, Y2: 1f), (X1: 1f, Y1: 0f, X2: 1f, Y2: 1f) })
{
    foreach (float t in new[] { -1f, 0f, 1f, 2f })
        Check(NativeHudLayout.Ease(t, curve.X1, curve.Y1, curve.X2, curve.Y2) == (t <= 0 ? 0 : 1),
            "Cubic easing endpoint was not clamped exactly");
    float previous = 0;
    for (int i = 0; i <= 100; i++)
    {
        float eased = NativeHudLayout.Ease(i / 100f, curve.X1, curve.Y1, curve.X2, curve.Y2);
        Check(eased >= previous && eased <= 1, "Cubic easing must be bounded and monotonic");
        previous = eased;
    }
}
foreach (float t in new[] { .1f, .25f, .5f, .75f, .9f })
    Near(NativeHudLayout.Ease(t, 0, 0, 1, 1), t, "Linear cubic easing");
Near(NativeHudLayout.Ease(.5f, .42f, 0, .58f, 1), .5f, "Symmetric cubic midpoint");
// At parameter .5, this curve has x=.3125 and y=.5375; input is x, not the parameter.
Near(NativeHudLayout.Ease(.3125f, .25f, .1f, .25f, 1), .5375f, "Cubic easing must invert x");
Near(NativeHudLayout.Ease(.125f, 0, 0, 0, 1), .5f, "Cubic easing with flat initial x");
Near(NativeHudLayout.Ease(.875f, 1, 0, 1, 1), .5f, "Cubic easing with flat final x");

var screens = new[]
{
    (Name: "1080p", Width: 1920, Height: 1080, WideScale: 1920f / 1836),
    (Name: "1440p", Width: 2560, Height: 1440, WideScale: 1920f / 1836),
    (Name: "ultrawide", Width: 3440, Height: 1440, WideScale: 2580f / 1836),
    (Name: "portrait", Width: 1080, Height: 1920, WideScale: 607.5f / 1836)
};
foreach (var screen in screens)
{
    float defaultScale = NativeHudLayout.FitScale(1, screen.Width, screen.Height, 840, 174);
    Near(defaultScale, screen.Name == "portrait" ? 607.5f / 880 : 1,
        screen.Name + " default scale");
    Near(NativeHudLayout.FitScale(2, screen.Width, screen.Height, 1796, 174), screen.WideScale,
        screen.Name + " wide panel must fit width including margins");
    Near(NativeHudLayout.FitScale(2, screen.Width, screen.Height, 840, 2040), 1080f / 2080,
        screen.Name + " tall panel must fit height including margins");
    Near(NativeHudLayout.FitScale(.5f, screen.Width, screen.Height, 840, 174), .5f,
        screen.Name + " must not enlarge the requested scale");

    foreach (float width in new[] { 840f, 1796f })
    foreach (float height in new[] { 174f, 2040f })
    {
        float scale = NativeHudLayout.FitScale(2, screen.Width, screen.Height, width, height);
        float viewportHeight = 1080 / scale;
        float viewportWidth = viewportHeight * screen.Width / screen.Height;
        string label = $"{screen.Name}, panel {width}x{height}";
        Check(width + 40 <= viewportWidth + .001f, label + " overflowed width with margins");
        Check(height + 40 <= viewportHeight + .001f, label + " overflowed height with margins");
        foreach (float offset in new[] { -10000f, 0f, 50f, 10000f })
        {
            float x = NativeHudLayout.ClampX(offset, viewportWidth, width);
            float y = NativeHudLayout.ClampY(offset, viewportHeight, height);
            Check((viewportWidth - width) / 2 - Math.Abs(x) >= 20 - .001f,
                label + " horizontal offset violated margins");
            Check(y >= 20 && y + height <= viewportHeight - 20 + .001f,
                label + " vertical offset violated margins");
        }
    }
}

Near(NativeHudLayout.ClampX(-1000, 1920, 840), -520, "Left offset limit");
Near(NativeHudLayout.ClampX(1000, 1920, 840), 520, "Right offset limit");
Near(NativeHudLayout.ClampX(123, 1920, 840), 123, "Valid X offset changed");
Near(NativeHudLayout.ClampY(-1000, 1080, 174), 20, "Top offset limit");
Near(NativeHudLayout.ClampY(1000, 1080, 174), 886, "Bottom offset limit");
Near(NativeHudLayout.ClampY(123, 1080, 174), 123, "Valid Y offset changed");

// Exact-fit margins leave no room for movement; undersized viewports use safe fallback positions.
Near(NativeHudLayout.FitScale(2, 1920, 1080, 1880, 1040), 1, "Exact fit on both axes");
Near(NativeHudLayout.ClampX(50, 880, 840), 0, "Exact-fit X offset");
Near(NativeHudLayout.ClampY(50, 214, 174), 20, "Exact-fit Y offset");
Near(NativeHudLayout.ClampX(-50, 800, 840), 0, "Oversized panel must remain centered");
Near(NativeHudLayout.ClampY(50, 100, 174), 20, "Oversized panel must retain top margin");
Near(NativeHudLayout.FitScale(0, 1920, 1080, 840, 174), .01f, "Zero scale lower bound");
Near(NativeHudLayout.FitScale(-1, 1920, 1080, 840, 174), .01f, "Negative scale lower bound");
Near(NativeHudLayout.FitScale(1, 0, 0, 840, 174), 1, "Zero screen dimensions fallback");
Near(NativeHudLayout.FitScale(1, 0, 1080, 840, 174), .01f, "Degenerate width scale lower bound");
Console.WriteLine("Native HUD layout checks passed.");
