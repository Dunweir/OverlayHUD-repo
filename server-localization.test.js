const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const { Readable } = require('node:stream');
const { EventEmitter } = require('node:events');

const monsterNames = require('./assets/monster-localization.json');
const upgradeNames = require('./assets/upgrade-localization.json');

function boot() {
    const broadcasts = [], writes = [];
    const context = vm.createContext({
        require: (name) => name === 'fs' ? {
            mkdirSync() {}, writeFileSync: (file, text) => writes.push({ file, text })
        } : require(name),
        module: { exports: {} }, process, console, __dirname
    });
    vm.runInContext(fs.readFileSync(`${__dirname}/server.js`, 'utf8') + '\nthis.server = server;', context);
    function request(method, url, payload) {
        const stream = Readable.from(payload === undefined ? [] : [JSON.stringify(payload)]);
        stream.method = method;
        stream.url = url;
        return new Promise((resolve) => {
            let status;
            context.server.emit('request', stream, {
                writeHead(code) { status = code; },
                end(body) { resolve({ status, body: JSON.parse(body) }); }
            });
        });
    }
    const events = new EventEmitter();
    events.method = 'GET';
    events.url = '/api/events';
    context.server.emit('request', events, {
        writeHead(code) { assert.equal(code, 200); },
        write(data) { if (data.startsWith('data: ')) broadcasts.push(JSON.parse(data.slice(6))); },
        end() {}
    });
    return {
        broadcasts, writes,
        post: (url, payload) => request('POST', url, payload),
        state: async () => {
            const result = await request('GET', '/api/state');
            assert.equal(result.status, 200);
            return result.body.state;
        }
    };
}

test('API exposes official bundled ru/en names before plugin sync, with stable identities', async () => {
    const { state } = boot();
    const current = await state();
    assert.deepEqual(upgradeNames, require('./tests/localization/upgrade-names.json'));
    for (const [key, names] of Object.entries(monsterNames)) {
        const canonical = key === 'ENEMY.GNOME' ? 'Gnomes' : key === 'ENEMY.SNATCHER' ? 'Snatcher' : names.en;
        assert.deepEqual(current.hudNames.monsters[canonical], names, canonical);
    }
    for (const [key, { en, ru }] of Object.entries(upgradeNames)) {
        assert.deepEqual(current.hudNames.upgrades[key], { en, ru }, key);
    }
    assert.equal(Object.keys(current.hudNames.upgrades).length, 12);
    assert.deepEqual(current.hudNames.monsters.Pony, monsterNames['ENEMY.SNATCHER']);
    assert.deepEqual(current.hudNames.monsters.Animals, monsterNames['ENEMY.ANIMAL']);
    assert.deepEqual(current.monsters, []);
    assert.deepEqual(current.roster, []);
    assert.equal(current.itemDestroyedAlertsVisible, false);
});

test('server-generated entries localize all 30 monsters and 12 upgrades in tooltips, including unknown icons', async () => {
    const { post, state } = boot();
    await post('/api/settings', { settings: { saveTimestampsEnabled: false } });
    const html = fs.readFileSync(`${__dirname}/overlay.html`, 'utf8');
    const extract = (name) => {
        const start = html.indexOf(`        function ${name}(`);
        const end = html.indexOf('\n        }', start) + '\n        }'.length;
        assert(start >= 0 && end > start, name);
        return html.slice(start, end);
    };
    const fields = new Map(['.monster-tooltip-name', '.monster-tooltip-health', '.monster-tooltip-strength']
        .map(key => [key, { textContent: '' }]));
    const square = { querySelector: () => ({ querySelector: key => fields.get(key) }) };
    const ui = vm.createContext({
        currentLanguage: 'ru', latestState: null,
        // Deliberately distinct fallback: upgrades must resolve the server's names.
        interfaceText: { ru: { upgradesNames: {} }, en: { upgradesNames: {} } },
        getMonsterHealthText: () => '', getMonsterStrengthText: () => ''
    });
    vm.runInContext(['getLocalizedHudName', 'getMonsterName', 'getUpgradeName', 'updateMonsterTooltip']
        .map(extract).join('\n'), ui);
    assert.equal(Object.keys(monsterNames).length, 30);
    const cases = Object.entries(monsterNames).map(([key, labels]) => ({
        name: key === 'ENEMY.SNATCHER' ? 'Snatcher' : labels.en, labels
    }));
    cases.push({ name: 'Pony', labels: monsterNames['ENEMY.SNATCHER'] },
        { name: 'Modded Enemy', labels: { ru: 'Modded Enemy', en: 'Modded Enemy' } });
    for (const { name, labels } of cases) {
        // Reset slot limits so every identity goes through the actual seen-event path.
        assert.equal((await post('/api/level', { level: 9 })).status, 200);
        const result = await post('/api/monster-seen', { id: 'tooltip-monster', name });
        assert.equal(result.body.added, true, name);
        ui.latestState = await state();
        assert.equal(ui.latestState.monsters.length, 1, name);
        const monster = ui.latestState.monsters[0];
        if (['Snatcher', 'Pony', 'Modded Enemy'].includes(name)) assert.equal(monster.unknown, true, name);
        const before = JSON.stringify(ui.latestState);
        for (const language of ['ru', 'en']) {
            ui.currentLanguage = language;
            ui.updateMonsterTooltip(square, monster, {});
            assert.equal(fields.get('.monster-tooltip-name').textContent, labels[language], `${name}/${language}`);
        }
        assert.equal(JSON.stringify(ui.latestState), before, 'Tooltip must not mutate gameplay state');
    }
    assert.equal(Object.keys(upgradeNames).length, 12);
    assert.equal((await post('/api/upgrades', {
        upgrades: Object.fromEntries(Object.keys(upgradeNames).map(key => [key, 1]))
    })).status, 200);
    ui.latestState = await state();
    for (const language of ['ru', 'en']) {
        ui.currentLanguage = language;
        for (const [key, labels] of Object.entries(upgradeNames)) {
            assert.equal(ui.latestState[key], 1, key);
            assert.equal(ui.getUpgradeName({ key, name: 'Missing server translation' }), labels[language], `${key}/${language}`);
        }
    }
});

for (const language of ['ru', 'en']) {
    test(`localization changes reach API and SSE with ${language} already selected, without gameplay/timestamp changes`, async () => {
        const { post, state, broadcasts, writes } = boot();
        await post('/api/settings', { settings: { interfaceLanguage: language } });
        await post('/api/monster-seen', { name: 'Gnome', id: 'gnome-1' });
        await post('/api/upgrades', { upgrades: { extraJump: 3 } });
        const before = await state();
        const count = broadcasts.length;
        const payload = {
            names: { 'ENEMY.GNOME': { ru: 'Обновлённый гном', en: 'Updated Gnome' } },
            upgradeNames: { 'STATS.UPGRADE.EXTRA_JUMP': { ru: 'Обновлённый прыжок', en: 'Updated Jump' } }
        };
        assert.equal((await post('/api/monster-localization', payload)).status, 200);
        const after = await state();
        assert.equal(broadcasts.length, count + 1);
        assert.deepEqual(broadcasts.at(-1), after);
        assert.deepEqual(after.hudNames.monsters.Gnomes, payload.names['ENEMY.GNOME']);
        assert.deepEqual(after.hudNames.upgrades.extraJump, payload.upgradeNames['STATS.UPGRADE.EXTRA_JUMP']);
        assert.equal(after.hudNames.monsters['Updated Gnome'], undefined);
        assert.deepEqual({ ...after, hudNames: before.hudNames }, before);
        assert.equal(writes.length, 0);
        // The plugin republishes the same tables every minute; don't rerender on no-op syncs.
        await post('/api/monster-localization', payload);
        assert.equal(broadcasts.length, count + 1);
        await post('/api/monster-localization', {
            ...payload, upgradeNames: { 'STATS.UPGRADE.EXTRA_JUMP': { ru: 'Следующий прыжок' } }
        });
        assert.equal(broadcasts.length, count + 2);
        assert.equal(broadcasts.at(-1).hudNames.upgrades.extraJump.ru, 'Следующий прыжок');
        assert.equal(broadcasts.at(-1).interfaceLanguage, language);
    });
}

test('legacy names-only payload retains upgrades and fills missing labels from bundled assets', async () => {
    const { post, state, broadcasts } = boot();
    await post('/api/monster-localization', {
        names: {}, upgradeNames: { 'STATS.UPGRADE.LAUNCH': { en: 'Updated Launch' } }
    });
    assert.equal((await post('/api/monster-localization', {
        names: { 'ENEMY.BANG': { en: 'Updated Banger' } }
    })).status, 200);
    const current = await state();
    assert.deepEqual(current.hudNames.monsters.Banger, { en: 'Updated Banger', ru: 'Взрывник' });
    assert.deepEqual(current.hudNames.monsters.Animal, monsterNames['ENEMY.ANIMAL']);
    assert.deepEqual(current.hudNames.upgrades.tumbleLaunch, { en: 'Updated Launch', ru: 'Рывок при кувырке' });
    assert.deepEqual(current.hudNames.upgrades.extraJump, { en: 'Extra Jump', ru: 'Ещё один прыжок' });
    assert.deepEqual(broadcasts.at(-1), current);
});

test('invalid optional upgrade tables are rejected atomically without SSE or timestamp writes', async () => {
    const { post, state, broadcasts, writes } = boot();
    const before = await state();
    for (const invalid of [null, [], { 'STATS.UPGRADE.LAUNCH': [] },
        { 'ITEM.UPGRADE_LAUNCH': { en: 'Wrong table' } },
        { 'STATS.UPGRADE.LAUNCH': { ru: 'Bad\nName' } },
        { 'STATS.UPGRADE.LAUNCH': { en: '' } }]) {
        const result = await post('/api/monster-localization', {
            names: { 'ENEMY.GNOME': { en: 'Must not apply' } }, upgradeNames: invalid
        });
        assert.equal(result.status, 422);
        assert.deepEqual(await state(), before);
    }
    assert.equal(broadcasts.length, 0);
    assert.equal(writes.length, 0);
});

test('hudNames remain runtime-only across client state, settings and level resets', async () => {
    const { post, state } = boot();
    await post('/api/monster-localization', {
        names: { 'ENEMY.GNOME': { en: 'Runtime Gnome' } },
        upgradeNames: { 'STATS.UPGRADE.LAUNCH': { en: 'Runtime Launch' } }
    });
    const expected = (await state()).hudNames;
    const stale = { monsters: { Gnomes: { en: 'Stale' } }, upgrades: {} };
    await post('/api/state', { state: { hudNames: stale, saveTimestampsEnabled: false } });
    assert.deepEqual((await state()).hudNames, expected);
    await post('/api/settings', { settings: { hudNames: stale } });
    assert.deepEqual((await state()).hudNames, expected);
    await post('/api/level', { level: 2 });
    assert.deepEqual((await state()).hudNames, expected);
});

test('saved true item-destroyed preference survives settings restore, legacy state and game updates', async () => {
    const { post, state, broadcasts } = boot();
    assert.equal((await state()).itemDestroyedAlertsVisible, false);
    await post('/api/settings', { settings: { itemDestroyedAlertsVisible: true, saveTimestampsEnabled: false } });
    await post('/api/settings', { settings: { interfaceLanguage: 'en' } });
    await post('/api/level', { level: 2 });
    assert.equal((await state()).itemDestroyedAlertsVisible, true);
    assert.equal(broadcasts.at(-1).itemDestroyedAlertsVisible, true);
    await post('/api/state', { state: { itemDestroyedAlertsVisible: true } });
    assert.equal((await state()).itemDestroyedAlertsVisible, true);
});
