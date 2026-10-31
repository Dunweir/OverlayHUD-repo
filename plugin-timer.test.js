const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const source = fs.readFileSync(path.join(__dirname, 'overlay-hud', 'Plugin.cs'), 'utf8');
const timer = source.slice(
    source.indexOf('private void SyncEnemyParentTimerChanged('),
    source.indexOf('private void SyncEnemyParentStatusChanged(')
);

test('frequent timer hook leaves metadata to full status updates', () => {
    assert.ok(timer.length > 0);
    assert.doesNotMatch(timer, /GetMonsterMetadata|AppendMonsterMetadata|GetEnemyTierJson/);
    assert.match(timer, /PostMonsterStatuses/);
    const status = source.slice(source.indexOf('private void SyncEnemyParentStatusChanged('));
    assert.match(status, /GetMonsterMetadata\(candidate\)/);
});

test('timer skips unchanged live enemies and throttles before allocating strings', () => {
    const liveGuard = timer.indexOf('if (!aliveChanged && alive) return;');
    const throttle = timer.indexOf('now - lastSentAt < 0.5f');
    const format = timer.indexOf('roundedRemaining.ToString(');
    assert.ok(liveGuard >= 0 && liveGuard < throttle);
    assert.ok(throttle >= 0 && throttle < format);
    assert.match(timer, /if \(!aliveChanged\s*&& lastTimerStatusBySourceId/);
    assert.match(timer, /lastAliveBySourceId\[instanceId\] = alive/);
    assert.match(timer, /lastTimerStatusSentAtBySourceId\[instanceId\] = now/);
});

test('periodic fallback checks one enemy without rebuilding immutable metadata', () => {
    const tick = source.slice(
        source.indexOf('private void TickScan('),
        source.indexOf('private void ResetSeenMonsters(')
    );
    assert.match(tick, /SyncMonsterStatuses\(new List<ResolvedEnemyCandidate>\(\), true\)/);

    const snapshot = source.slice(
        source.indexOf('private void SyncMonsterStatuses('),
        source.indexOf('private static bool TryGetEnemyRespawnStatus(')
    );
    assert.match(snapshot, /if \(fallbackSlice && sourceIds\.Count > 1\)/);
    assert.match(snapshot, /sourceIds\.Clear\(\);\s*sourceIds\.Add\(selectedId\)/);
    assert.match(snapshot, /if \(!fallbackSlice && !rosterMetadataBySourceId\.TryGetValue/);
    assert.match(snapshot, /lastFallbackStatusBySourceId/);
});

test('initial upgrades, roster and status snapshots are split across Unity frames', () => {
    const tick = source.slice(
        source.indexOf('private void TickScan('),
        source.indexOf('private void ResetSeenMonsters(')
    );
    assert.match(tick, /if \(pendingUpgradeSnapshot != null\) return;/);
    assert.match(tick, /pendingUpgradeSnapshot = StartCoroutine\(SyncPlayerUpgradesIfChanged\(keys\)\);\s*return;/);

    const known = source.slice(
        source.indexOf('private void SyncKnownEnemies('),
        source.indexOf('private void TryRevealUnknownEnemy(')
    );
    assert.match(known, /StartCoroutine\(PublishInitialMonsterState\(resolvedEnemies\)\)/);
    assert.match(known, /yield return null;[\s\S]*RevealAllKnownEnemies\(enemies\);[\s\S]*yield return null;[\s\S]*SyncMonsterStatuses\(enemies\)/);
});

test('initial upgrade reflection reads are limited to one table per frame', () => {
    const upgrades = source.slice(
        source.indexOf('private IEnumerator SyncPlayerUpgradesIfChanged('),
        source.indexOf('private static int? ResolveLocalPlayerUpgrade(')
    );
    assert.match(upgrades, /ResolveLocalPlayerUpgrade\(statsManager, steamId, binding\.Value\);[^]*?yield return null;\s*if \(!gameplayActive\)/);
    assert.match(upgrades, /pendingUpgradeSnapshot = null;/);
    assert.match(source, /StopCoroutine\(pendingUpgradeSnapshot\);\s*pendingUpgradeSnapshot = null;/);
});

test('roster metadata is cached and reused by the initial status snapshot', () => {
    assert.match(source, /rosterMetadataBySourceId\.TryGetValue\(instanceId, out string metadata\)/);
    assert.match(source, /rosterMetadataBySourceId\[instanceId\] = metadata/);
    assert.match(source, /!rosterMetadataBySourceId\.TryGetValue\(instanceId, out statusMetadata\)/);
    assert.match(source, /rosterMetadataBySourceId\.Clear\(\)/);
});
