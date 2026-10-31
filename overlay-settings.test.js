const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');

const source = fs.readFileSync(`${__dirname}/overlay-state.js`, 'utf8');
const plain = (value) => JSON.parse(JSON.stringify(value));
const flush = () => new Promise((resolve) => setImmediate(resolve));
const game = {
    level: 9, gameplayVisible: true, tabHidden: true, currencyVisible: true,
    running: true, seconds: 42, startedAt: 123456,
    monsters: [{ id: 'enemy-1', name: 'Banger', health: 50 }],
    roster: [{ id: 'enemy-2', name: 'Gnome' }], rosterPending: true,
    mapValue: 1200, mapValueInitial: 1500, mapValueGoal: 900, lostValue: 300,
    lastDestroyedItem: { player: 'Player', item: 'Vase', at: 123 },
    strength: 5, health: 3, stamina: 4, range: 2, tumbleLaunch: 1
};
const settings = {
    interfaceLanguage: 'en', saveTimestampsEnabled: false, bgEnabled: true,
    timerVisible: false, compactModeEnabled: false, upgradeLayout: 'stacked',
    upgradeRows: 'single', squareSize: 88, upgradeSize: 47, columnsCount: 5,
    overlayScaleVersion: 3, columnsLayoutVersion: 2, overlayDefaultsVersion: 4,
    overlayAlignment: 'right', overlayPosition: { left: 55, top: 80, anchorX: 'right', anchorY: 'bottom' },
    controlsPosition: { left: 12, top: 34, anchorX: null, anchorY: null },
    hoverOpacity: 35, monsterHealthBarsVisible: false, valueWrapEnabled: false,
    upgradeVisibility: { health: false }
};

function boot({ stored = null, remote = {}, get, post, blocked = false } = {}) {
    const posts = [], streams = [], broadcasts = [], events = {};
    let channel;
    class EventSource {
        constructor() { streams.push(this); }
        send(value) { this.onmessage({ data: JSON.stringify(value) }); }
    }
    class BroadcastChannel {
        constructor() { channel = this; }
        addEventListener(_, handler) { this.receive = handler; }
        postMessage(value) { broadcasts.push(plain(value)); }
    }
    const context = vm.createContext({
        window: { location: { protocol: 'http:' }, EventSource, BroadcastChannel, addEventListener: (name, handler) => { events[name] = handler; } },
        EventSource, BroadcastChannel, console,
        localStorage: {
            getItem() { if (blocked) throw Error('blocked'); return stored; },
            setItem(_, value) { if (blocked) throw Error('blocked'); stored = value; }
        },
        fetch: async (url, options) => {
            if (options.method === 'POST') {
                assert.equal(url, '/api/settings', 'never replace live server state');
                const body = JSON.parse(options.body);
                assert.deepEqual(Object.keys(body), ['settings']);
                posts.push(body.settings);
                for (const key of Object.keys(game)) assert.equal(key in body.settings, false, key);
                return post ? post(body.settings) : { ok: true };
            }
            assert.equal(url, '/api/state');
            return get ? get() : { ok: true, json: async () => ({ state: remote }) };
        }
    });
    vm.runInContext(source + '\nthis.api = OverlayApp;', context);
    return { api: context.api, posts, streams, broadcasts, events, channel,
        stored: () => stored, setStored: (value) => { stored = value; } };
}
function assertSubset(actual, expected) {
    for (const [key, value] of Object.entries(expected)) assert.deepEqual(plain(actual[key]), plain(value), key);
}

test('restart restores preferences but never stored gameplay, including before GET resolves', async () => {
    let finish;
    const client = boot({ stored: JSON.stringify({ ...game, ...settings }),
        get: () => new Promise((resolve) => { finish = resolve; }) });
    const before = client.api.getState();
    assert.equal(before.level, 1);
    assert.equal(before.health, 0);
    assert.equal(before.seconds, 0);
    assert.equal(before.mapValue, 0);
    assert.deepEqual(plain(before.monsters), []);
    finish({ ok: true, json: async () => ({ state: { ...game, level: 12, interfaceLanguage: 'ru' } }) });
    await flush();
    assertSubset(client.api.getState(), { ...game, level: 12 });
    assertSubset(client.api.getState(), { ...settings, upgradeVisibility: client.api.getState().upgradeVisibility });
    assert.equal(client.api.getState().upgradeVisibility.health, false);
    const saved = JSON.parse(client.stored());
    for (const key of Object.keys(game)) assert.equal(key in saved, false, key);
    assert.equal(client.posts.length, 1);
    client.streams[0].send({ ...game, interfaceLanguage: 'ru', saveTimestampsEnabled: true });
    assert.equal(client.api.getState().interfaceLanguage, 'en');
    assert.equal(client.api.getState().saveTimestampsEnabled, false);
    const restarted = boot({ stored: client.stored(), remote: { ...game, level: 15 } });
    await flush();
    assert.equal(restarted.api.getState().squareSize, 88);
    assert.equal(restarted.api.getState().level, 15);
});

for (const stored of [null, '{}', '{bad', 'null', '[]', '42']) {
    test(`missing/corrupt storage uses server settings and current gameplay: ${stored}`, async () => {
        const client = boot({ stored, remote: { ...game, ...settings } });
        await flush();
        assertSubset(client.api.getState(), game);
        assert.equal(client.api.getState().interfaceLanguage, 'en');
        assert.equal(client.api.getState().squareSize, 88);
        assert.equal(client.posts.length, 0);
    });
}

test('blocked storage keeps live state and tray preferences in memory', async () => {
    const client = boot({ blocked: true, remote: game });
    await flush();
    client.api.setInterfaceLanguage('en');
    client.api.setSaveTimestampsEnabled(false);
    await flush();
    assertSubset(client.api.getState(), game);
    assert.equal(client.api.getState().interfaceLanguage, 'en');
    assert.equal(client.posts.at(-1).saveTimestampsEnabled, false);
});

test('tray setters during startup and an in-flight POST win over stale snapshots', async () => {
    let finishGet;
    const pending = [];
    const client = boot({
        get: () => new Promise((resolve) => { finishGet = resolve; }),
        post: () => new Promise((resolve) => pending.push(resolve))
    });
    client.api.setInterfaceLanguage('en');
    finishGet({ ok: true, json: async () => ({ state: game }) });
    await flush();
    assert.equal(client.posts.length, 1);
    client.api.setInterfaceLanguage('ru');
    client.api.setSaveTimestampsEnabled(false);
    client.streams[0].send({ ...game, level: 20, interfaceLanguage: 'en', saveTimestampsEnabled: true });
    assert.equal(client.posts.length, 1, 'writes are serialized');
    pending.shift()({ ok: true });
    await flush();
    assert.equal(client.posts.length, 2);
    assert.equal(client.posts[1].interfaceLanguage, 'ru');
    assert.equal(client.posts[1].saveTimestampsEnabled, false);
    assert.equal(client.api.getState().level, 20);
    pending.shift()({ ok: true });
    await flush();
    assert.equal(client.posts.length, 2);
});

test('unavailable settings endpoint preserves preferences and still receives gameplay', async () => {
    const client = boot({ stored: JSON.stringify(settings), remote: game, post: async () => ({ ok: false }) });
    await flush();
    assert.equal(client.streams.length, 1);
    client.streams[0].send({ ...game, level: 25 });
    assert.equal(client.api.getState().level, 25);
    assert.equal(client.api.getState().saveTimestampsEnabled, false);
    client.streams[0].onopen();
    await flush();
    assert.equal(client.posts.length, 2);
});

test('storage and broadcast settings updates cannot resurrect another tab gameplay', async () => {
    const client = boot({ remote: game });
    await flush();
    client.setStored(JSON.stringify({ ...settings, level: 99, monsters: [] }));

    // Use the actual storage key from the source so renaming it does not hide a regression.
    client.events.storage({ key: source.match(/const storageKey = "([^"]+)"/)[1] });
    assertSubset(client.api.getState(), game);
    assert.equal(client.api.getState().squareSize, 88);
    client.channel.receive({ data: { interfaceLanguage: 'ru', level: 100, monsters: [] } });
    assertSubset(client.api.getState(), game);
    assert.equal(client.api.getState().interfaceLanguage, 'ru');
    client.api.setSaveTimestampsEnabled(true);
    for (const key of Object.keys(game)) assert.equal(key in client.broadcasts.at(-1), false, key);
    await flush();
});
