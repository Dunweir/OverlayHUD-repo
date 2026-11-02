const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const { Readable, PassThrough } = require('node:stream');
const { EventEmitter } = require('node:events');

const plain = (value) => JSON.parse(JSON.stringify(value));
const serverSource = fs.readFileSync(`${__dirname}/server.js`, 'utf8');
const clientSource = fs.readFileSync(`${__dirname}/overlay-state.js`, 'utf8');
const flush = () => new Promise((resolve) => setImmediate(resolve));

function boot() {
    const writes = [], broadcasts = [];
    const context = vm.createContext({
        require: (name) => name === 'fs' ? {
            mkdirSync() {}, writeFileSync: (file, text) => writes.push({ file, text })
        } : require(name),
        module: { exports: {} }, process, console, __dirname
    });
    vm.runInContext(serverSource + `\nthis.api = { server, preferenceKeys, getNormalizedOverlayState,
        setGameLevel, setGameplayVisibility, stopOverlayServer, writeTimestampSession };`, context);
    const api = context.api;
    function dispatch(url, stream) {
        stream.method = 'POST';
        stream.url = url;
        return new Promise((resolve) => {
            let status;
            api.server.emit('request', stream, {
                writeHead(code) { status = code; },
                end(body) { resolve({ status, body: JSON.parse(body) }); }
            });
        });
    }
    const post = (url, body) => dispatch(url, Readable.from([JSON.stringify(body)]));
    const settings = (value) => post('/api/settings', { settings: value });
    const get = () => plain(api.getNormalizedOverlayState());
    const request = new EventEmitter();
    request.method = 'GET';
    request.url = '/api/events';
    api.server.emit('request', request, {
        writeHead(code) { assert.equal(code, 200); },
        write(data) { if (data.startsWith('data: ')) broadcasts.push(JSON.parse(data.slice(6))); },
        end() {}
    });
    return { api, writes, broadcasts, dispatch, post, settings, get };
}

const gameplay = {
    level: 12, gameplayVisible: true, tabHidden: true, currencyVisible: true,
    seconds: 75, running: true, startedAt: 123456,
    monsters: [{ name: 'Gnome', id: 'enemy-1', health: 23, maxHealth: 100, strength: 7 }],
    roster: [{ name: 'Gnome', sourceIds: ['enemy-1'], health: 23, strength: 7 }], rosterPending: true,
    mapValue: 1500, mapValueInitial: 2000, mapValueGoal: 1000, lostValue: 500,
    lastDestroyedItem: { player: 'Player', item: 'Vase', at: 123 },
    strength: 3, health: 5, stamina: 4, range: 2, tumbleLaunch: 1
};

test('settings allowlist matches the client and excludes every gameplay field', () => {
    const { api } = boot();
    const declaration = clientSource.match(/const preferenceKeys = \[[\s\S]*?\];/)[0];
    const clientKeys = vm.runInNewContext(declaration + '\npreferenceKeys');
    assert.deepEqual(Array.from(api.preferenceKeys), Array.from(clientKeys));
    for (const key of Object.keys(gameplay)) assert.equal(api.preferenceKeys.includes(key), false, key);
});

test('settings endpoint merges preferences, preserves gameplay and broadcasts the merged state', async () => {
    const { post, settings, get, broadcasts } = boot();
    await post('/api/state', { state: { ...gameplay, bgEnabled: true } });
    const before = get();
    const result = await settings(JSON.parse(JSON.stringify({
        ...Object.fromEntries(Object.keys(gameplay).map((key) => [key, null])),
        interfaceLanguage: 'en', saveTimestampsEnabled: false, squareSize: 85,
        overlayPosition: { left: 20, top: 30, anchorX: 'right', anchorY: 'bottom' },
        upgradeVisibility: { health: false }, unexpected: 'ignored', constructor: 'ignored',
        currencyRect: { x: 1 },
        ['__proto__']: { polluted: true }
    })));
    assert.equal(result.status, 200);
    assert.deepEqual(result.body, { ok: true });
    const after = get();
    for (const key of Object.keys(gameplay)) assert.deepEqual(after[key], before[key], key);
    assert.equal(after.interfaceLanguage, 'en');
    assert.equal(after.saveTimestampsEnabled, false);
    assert.equal(after.squareSize, 85);
    assert.equal(after.bgEnabled, true, 'unspecified settings are retained');
    assert.equal(after.upgradeVisibility.health, false);
    assert.equal(after.upgradeVisibility.strength, true);
    assert.deepEqual(after.overlayPosition, { left: 20, top: 30, anchorX: 'right', anchorY: 'bottom' });
    for (const key of ['unexpected', 'constructor', '__proto__', 'currencyRect', 'polluted']) {
        assert.equal(Object.hasOwn(after, key), false, key);
    }
    assert.equal(broadcasts.length, 2);
    assert.deepEqual(broadcasts.at(-1), after);
    await settings({});
    assert.deepEqual(get(), after);
});

test('game updates arriving during a settings request cannot be rolled back', async () => {
    const { dispatch, post, get } = boot();
    const body = new PassThrough();
    const response = dispatch('/api/settings', body);
    body.write('{"settings":{"interfaceLanguage":');
    await post('/api/state', { state: gameplay });
    body.end('"en","level":1,"monsters":[]}}');
    assert.equal((await response).status, 200);
    const state = get();
    for (const [key, value] of Object.entries(gameplay)) assert.deepEqual(state[key], value, key);
    assert.equal(state.interfaceLanguage, 'en');
});

test('invalid settings requests leave state, timestamps and broadcasts untouched', async () => {
    const { settings, dispatch, get, writes, broadcasts } = boot();
    const before = get();
    for (const value of [undefined, null, [], false, 12, 'en']) {
        assert.equal((await settings(value)).status, 422);
        assert.deepEqual(get(), before);
    }
    assert.equal((await dispatch('/api/settings', Readable.from(['{broken']))).status, 400);
    assert.equal(writes.length, 0);
    assert.equal(broadcasts.length, 0);
    assert.deepEqual(get(), before);
});

test('settings normalization uses existing preference rules', async () => {
    const { settings, get } = boot();
    await settings({ interfaceLanguage: 'invalid', hoverOpacity: 999, saveTimestampsEnabled: 'false',
        compactModeEnabled: false, upgradeRows: 'single' });
    const state = get();
    assert.equal(state.interfaceLanguage, 'ru');
    assert.equal(state.hoverOpacity, 100);
    assert.equal(state.saveTimestampsEnabled, true);
    assert.equal(state.upgradeLayout, 'stacked');
    assert.equal(state.upgradeRows, 'single');
});

test('settings disable finalizes the original-language line once and resumes only next level', async () => {
    const { api, post, settings, get, writes } = boot();
    api.setGameLevel(1, 'Headman Manor');
    await post('/api/state', { state: { ...get(), monsters: [{ name: 'Gnome', count: 4 }] } });
    await settings({ interfaceLanguage: 'en', saveTimestampsEnabled: false });
    assert.match(writes.at(-1).text, /Уровень 1 Manor \[Гном x4\]\n$/);
    const saved = writes.at(-1).text;
    const count = writes.length;
    await settings({ saveTimestampsEnabled: false });
    api.setGameLevel(2, 'Museum');
    api.setGameplayVisibility(false);
    api.writeTimestampSession();
    await api.stopOverlayServer();
    assert.equal(writes.length, count);
    await settings({ saveTimestampsEnabled: true });
    api.setGameplayVisibility(false);
    await api.stopOverlayServer();
    assert.equal(writes.length, count);
    api.setGameLevel(3, 'Museum');
    assert.ok(writes.at(-1).text.startsWith(saved));
    assert.match(writes.at(-1).text, /Level 3 Museum\n$/);
    assert.doesNotMatch(writes.at(-1).text, /(?:Level|Уровень) 2/);
});

test('settings disabled before gameplay creates no timestamp files', async () => {
    const { api, settings, writes } = boot();
    await settings({ saveTimestampsEnabled: false });
    api.setGameLevel(1, 'Manor');
    api.setGameLevel(2, 'Museum');
    await api.stopOverlayServer();
    assert.equal(writes.length, 0);
});

test('stored client preferences reach the real settings handler without restoring gameplay', async () => {
    const { post, get, api } = boot();
    await post('/api/state', { state: gameplay });
    let stored = JSON.stringify({ ...gameplay, level: 99, monsters: [], squareSize: 91,
        interfaceLanguage: 'en', saveTimestampsEnabled: false });
    const pending = [];
    const context = vm.createContext({
        window: { location: { protocol: 'http:' }, addEventListener() {} }, console,
        localStorage: { getItem: () => stored, setItem: (_, value) => { stored = value; } },
        fetch: async (url, options) => {
            if (options.method !== 'POST') return { ok: true, json: async () => ({ state: get() }) };
            assert.equal(url, '/api/settings');
            const request = post(url, JSON.parse(options.body));
            pending.push(request);
            const result = await request;
            return { ok: result.status === 200 };
        }
    });
    vm.runInContext(clientSource + '\nthis.api = OverlayApp;', context);
    await flush();
    await Promise.all(pending);
    for (const [key, value] of Object.entries(gameplay)) assert.deepEqual(get()[key], value, key);
    assert.equal(get().squareSize, 91);
    assert.equal(get().interfaceLanguage, 'en');
    assert.equal(get().saveTimestampsEnabled, false);
    context.api.setInterfaceLanguage('ru');
    context.api.setSaveTimestampsEnabled(true);
    await flush();
    await Promise.all(pending);
    assert.equal(get().interfaceLanguage, 'ru');
    assert.equal(get().saveTimestampsEnabled, true);
    assert.equal(JSON.parse(stored).monsters, undefined);
    await api.stopOverlayServer();
});
