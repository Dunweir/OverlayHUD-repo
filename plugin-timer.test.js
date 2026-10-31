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
    assert.match(snapshot, /fallbackSlice \? "" : GetMonsterMetadata\(candidate\)/);
    assert.match(snapshot, /lastFallbackStatusBySourceId/);
});
