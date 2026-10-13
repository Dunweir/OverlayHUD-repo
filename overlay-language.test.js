const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');

const flush = () => new Promise((resolve) => setImmediate(resolve));
const plain = (value) => JSON.parse(JSON.stringify(value));

function loadClient({ stored = null, serverState = {}, postResult = async () => ({ ok: true }) } = {}) {
    let remote = plain(serverState);
    const posts = [];
    const streams = [];
    const storageWrites = [];
    class EventSource {
        constructor(url) {
            assert.equal(url, '/api/events');
            streams.push(this);
            queueMicrotask(() => {
                this.onopen();
                this.send(remote);
            });
        }
        send(state) {
            this.onmessage({ data: JSON.stringify(state) });
        }
    }
    const context = vm.createContext({
        window: { location: { protocol: 'http:' }, EventSource, addEventListener() {} },
        EventSource,
        localStorage: {
            getItem: () => stored,
            setItem: (_, value) => { stored = value; storageWrites.push(JSON.parse(value)); }
        },
        fetch: async (url, options) => {
            assert.equal(url, '/api/state');
            if (options.method === 'POST') {
                const state = JSON.parse(options.body).state;
                posts.push(state);
                const response = await postResult();
                if (response.ok) remote = state;
                return response;
            }
            return { ok: true, json: async () => ({ state: remote }) };
        },
        console
    });
    vm.runInContext(fs.readFileSync(`${__dirname}/overlay-state.js`, 'utf8') + '\nthis.api = OverlayApp;', context);
    return { api: context.api, posts, streams, storageWrites, stored: () => stored };
}

const gameplay = {
    level: 9, gameplayVisible: true, tabHidden: true, currencyVisible: true,
    running: true, seconds: 42, startedAt: 123456,
    monsters: [{ id: 'enemy-1', name: 'Banger', health: 50 }],
    roster: [{ id: 'enemy-2', name: 'Gnome' }], rosterPending: true,
    mapValue: 1200, mapValueInitial: 1500, mapValueGoal: 900, lostValue: 300,
    strength: 5, health: 3
};

function assertGameplay(state) {
    for (const [key, value] of Object.entries(gameplay)) {
        assert.deepEqual(plain(state[key]), value, key);
    }
}

test('restart restores saved English without restoring stale gameplay', async () => {
    const first = loadClient();
    await flush();
    first.api.setInterfaceLanguage('en');
    await flush();
    const restarted = loadClient({
        stored: first.stored(), serverState: { ...gameplay, interfaceLanguage: 'ru' }
    });
    await flush();
    assert.equal(restarted.api.getState().interfaceLanguage, 'en');
    assert.equal(JSON.parse(restarted.stored()).interfaceLanguage, 'en');
    assert.ok(restarted.storageWrites.every((state) => state.interfaceLanguage === 'en'));
    assert.equal(restarted.posts.length, 1);
    assert.equal(restarted.posts[0].interfaceLanguage, 'en');
    assertGameplay(restarted.api.getState());
    assertGameplay(restarted.posts[0]);
});

test('initial merged POST completes before opening SSE', async () => {
    let finishPost;
    const client = loadClient({
        stored: JSON.stringify({ interfaceLanguage: 'en', level: 1 }),
        serverState: { ...gameplay, interfaceLanguage: 'ru' },
        postResult: () => new Promise((resolve) => { finishPost = resolve; })
    });
    await flush();
    assert.equal(client.posts.length, 1);
    assert.equal(client.streams.length, 0);
    assert.equal(client.api.getState().interfaceLanguage, 'en');
    assertGameplay(client.api.getState());
    finishPost({ ok: true });
    await flush();
    assert.equal(client.streams.length, 1);
    assert.equal(client.api.getState().interfaceLanguage, 'en');
});

for (const stored of [null, '{}', '{invalid', 'null', '{"interfaceLanguage":"de"}']) {
    test(`no valid saved language uses server language (${stored})`, async () => {
        const client = loadClient({ stored, serverState: { ...gameplay, interfaceLanguage: 'en' } });
        await flush();
        assert.equal(client.api.getState().interfaceLanguage, 'en');
        assert.equal(client.posts.length, 0);
        assertGameplay(client.api.getState());
    });
}

test('missing local and server language falls back to Russian', async () => {
    const client = loadClient();
    await flush();
    assert.equal(client.api.getState().interfaceLanguage, 'ru');
    assert.equal(client.posts.length, 0);
});

test('saved Russian also takes precedence over initial server English', async () => {
    const client = loadClient({
        stored: '{"interfaceLanguage":"ru"}', serverState: { interfaceLanguage: 'en' }
    });
    await flush();
    assert.equal(client.api.getState().interfaceLanguage, 'ru');
    assert.equal(client.posts[0].interfaceLanguage, 'ru');
});

test('subsequent SSE and local language changes remain synchronized and persisted', async () => {
    const client = loadClient({
        stored: '{"interfaceLanguage":"en"}', serverState: { interfaceLanguage: 'ru' }
    });
    await flush();
    client.streams[0].send({ ...gameplay, interfaceLanguage: 'ru' });
    assert.equal(client.api.getState().interfaceLanguage, 'ru');
    assert.equal(JSON.parse(client.stored()).interfaceLanguage, 'ru');
    assertGameplay(client.api.getState());
    assert.equal(client.posts.length, 1);
    client.api.setInterfaceLanguage('en');
    await flush();
    assert.equal(client.posts.length, 2);
    assert.equal(client.posts[1].interfaceLanguage, 'en');
    assertGameplay(client.posts[1]);
    client.streams[0].send(client.posts[1]);
    assert.equal(client.api.getState().interfaceLanguage, 'en');
    assert.equal(JSON.parse(client.stored()).interfaceLanguage, 'en');
    assert.equal(client.posts.length, 2);
});

for (const failure of ['http', 'network']) {
    test(`failed startup POST does not allow SSE to overwrite saved language (${failure})`, async () => {
        const client = loadClient({
            stored: '{"interfaceLanguage":"en"}',
            serverState: { ...gameplay, interfaceLanguage: 'ru' },
            postResult: async () => {
                if (failure === 'network') throw new Error('offline');
                return { ok: false };
            }
        });
        await flush();
        assert.equal(client.streams.length, 0);
        assert.equal(client.api.getState().interfaceLanguage, 'en');
        assert.equal(JSON.parse(client.stored()).interfaceLanguage, 'en');
        assertGameplay(client.api.getState());
    });
}
