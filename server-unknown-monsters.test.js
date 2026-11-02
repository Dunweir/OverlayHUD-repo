const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const http = require('node:http');

async function fixture(t) {
    const writes = [];
    const context = vm.createContext({
        require: (name) => name === 'fs' ? {
            mkdirSync() {}, writeFileSync: (_, text) => writes.push(text)
        } : require(name),
        module: { exports: {} }, process, console, __dirname
    });
    vm.runInContext(fs.readFileSync(`${__dirname}/server.js`, 'utf8') + '\nthis.server = server;', context);
    const server = context.server;
    await new Promise((resolve) => server.listen(0, '127.0.0.1', resolve));
    t.after(() => context.module.exports.stopOverlayServer());
    async function request(route, payload) {
        return new Promise((resolve, reject) => {
            const req = http.request({ hostname: '127.0.0.1', port: server.address().port,
                path: `/api/${route}`, method: payload === undefined ? 'GET' : 'POST',
                headers: { 'Content-Type': 'application/json', Connection: 'close' }
            }, (res) => {
                let body = '';
                res.on('data', (chunk) => { body += chunk; });
                res.on('end', () => {
                    try { assert.equal(res.statusCode, 200, body); resolve(JSON.parse(body)); }
                    catch (error) { reject(error); }
                });
            });
            req.on('error', reject);
            req.end(payload === undefined ? undefined : JSON.stringify(payload));
        });
    }
    await request('level', { level: 1, levelName: 'Modded Museum Annex / 地下' });
    return { request, writes, state: async () => (await request('state')).state };
}

const eventOrders = [
    ['status', 'seen', 'roster'], ['seen', 'status', 'roster'],
    ['status', 'roster', 'seen'], ['roster', 'seen', 'status'],
    ['seen', 'roster', 'status'], ['roster', 'status', 'seen']
];

for (const name of ['Animal', 'New Tier Beast', 'Gnomes']) {
    for (const order of eventOrders) {
        test(`game tier for ${name}: ${order.join(' -> ')}`, async (t) => {
            const { request, state } = await fixture(t);
            await request('level', { level: 20 });
            const actions = {
                status: () => request('monster-status', { statuses: [{ id: 42, alive: true, tier: 3 }] }),
                seen: () => request('monster-seen', { id: 42, name }),
                roster: () => request('roster', { monsters: [{ id: 42, name, tier: 3 }] })
            };
            for (const action of order) {
                await actions[action]();
                const current = await state();
                if (!order.slice(0, order.indexOf(action) + 1).includes('seen')) {
                    assert.equal(current.monsters.length, 0, 'tiers must not reveal unseen monsters');
                }
                if (current.monsters.length && (action !== 'seen' || order[0] !== 'seen')) {
                    assert.equal(current.monsters[0].tier, 3);
                }
            }
            const initial = await state();
            const level = initial.monsters[0].level;
            const count = initial.monsters[0].count;
            async function check(tier) {
                const current = await state();
                assert.equal(current.monsters.length, 1);
                assert.equal(current.roster.length, 1);
                assert.equal(current.monsters[0].tier, tier);
                assert.equal(current.roster[0].tier, tier);
                assert.equal(current.monsters[0].level, level);
                assert.equal(current.monsters[0].count, count);
                assert.equal(current.monsters[0].sourceId, current.roster[0].id);
            }
            await check(3);
            await request('monster-status', { statuses: [{ id: 42, alive: false, respawnRemaining: 20 }] });
            await request('roster', { monsters: [{ id: 42, name }] });
            await check(3);
            await request('monster-status', { statuses: [{ id: 42, alive: true, tier: null }] });
            await request('roster', { monsters: [{ id: 42, name }] });
            await check(null);
            await request('roster', { monsters: [{ id: 42, name, tier: 2 }] });
            await request('monster-status', { statuses: [{ id: 42, alive: true }] });
            await check(2);
            await request('roster', { monsters: [{ id: 42, name, tier: null }] });
            await check(null);
            assert.equal((await actions.seen()).added, false);
            await request('level', { level: 20 });
            await actions.seen();
            assert.equal((await state()).monsters[0].tier, null);
        });
    }
}

for (const order of [
    ['status', 'seen', 'roster'], ['seen', 'status', 'roster'],
    ['status', 'roster', 'seen'], ['roster', 'seen', 'status'],
    ['seen', 'roster', 'status'], ['roster', 'status', 'seen']
]) {
    test(`unknown HTTP ordering: ${order.join(' -> ')}`, async (t) => {
        const { request, state } = await fixture(t);
        const name = 'Modded Beast / Тень (Clone)';
        const actions = {
            status: () => request('monster-status', { statuses: [{ id: 42, alive: true,
                health: 70, maxHealth: 120, unknown: true, strengthForceMax: 6, strength: 16 }] }),
            seen: () => request('monster-seen', { id: 42, name }),
            roster: () => request('roster', { monsters: [{ id: 42, name, unknown: true }] })
        };
        for (const action of order) {
            await actions[action]();
            if (!order.slice(0, order.indexOf(action) + 1).includes('seen')) {
                assert.equal((await state()).monsters.length, 0, 'do not reveal from status/roster');
            }
        }
        let current = await state();
        assert.equal(current.monsters.length, 1);
        assert.equal(current.roster.length, 1);
        const monster = current.monsters[0];
        assert.equal(monster.name, name);
        assert.equal(monster.unknown, true);
        assert.equal(monster.image, 'assets/monsters/new.webp');
        assert.equal(monster.level, 1);
        assert.equal(monster.strength, 9);
        assert.equal(monster.sourceId, current.roster[0].id);
        assert.equal(current.roster[0].health, 70);
        assert.equal(current.roster[0].maxHealth, 120);
        await request('monster-status', { statuses: [{ id: 42, alive: false, health: 0,
            respawnRemaining: 30, unknown: true, strength: null }] });
        await actions.roster();
        current = await state();
        assert.equal(current.roster[0].health, 0);
        assert.equal(current.roster[0].maxHealth, 120);
        assert.equal(current.roster[0].alive, false);
        assert.ok(current.roster[0].respawnEndsAt > Date.now());
        assert.equal(current.monsters[0].strength, undefined);
        assert.equal((await actions.seen()).added, false);
        await request('roster', { monsters: [{ id: 42, name, unknown: true, strengthForceMax: 6, strength: 21 }] });
        assert.equal((await state()).monsters[0].strength, 13);
        await request('monster-status', { statuses: [{ id: 42, alive: true, strengthForceMax: 6, strength: 10, maxHealth: 150 }] });
        current = await state();
        assert.equal(current.monsters[0].strength, 4);
        assert.equal(current.roster[0].health, 0);
        assert.equal(current.roster[0].maxHealth, 150);
        await request('level', { level: 2, levelName: 'Museum' });
        await actions.seen();
        current = await state();
        assert.equal(current.monsters[0].strength, undefined);
        assert.equal(current.roster[0].health, undefined);
    });
}

test('group tiers require agreement and do not change grouping or counts', async (t) => {
    const { request, state } = await fixture(t);
    await request('monster-seen', { id: 1, name: 'Gnomes' });
    assert.equal((await state()).monsters[0].tier, null);
    const members = [1, 2, 3, 4].map((id) => ({ id, name: 'Gnomes', tier: 2 }));
    await request('roster', { monsters: members });
    async function check(tier) {
        const current = await state();
        assert.equal(current.roster.length, 1);
        assert.equal(current.monsters.length, 1);
        assert.equal(current.roster[0].sourceIds.length, 4);
        assert.equal(current.roster[0].tier, tier);
        assert.equal(current.monsters[0].tier, tier);
        assert.equal(current.monsters[0].level, 1);
        assert.equal(current.monsters[0].count, 4);
    }
    await check(2);
    await request('monster-status', { statuses: [{ id: 2, alive: true, tier: 3 }] });
    await check(null);
    await request('roster', { monsters: members.map(({ tier, ...member }) => member) });
    await check(null);
    await request('monster-status', { statuses: [{ id: 2, alive: true, tier: 2 }] });
    await check(2);
    await request('roster', { monsters: [{ id: 3, name: 'Gnomes', tier: null }] });
    await check(null);
    await request('roster', { monsters: members });
    await check(2);
    assert.equal((await request('monster-seen', { id: 4, name: 'Gnomes' })).added, false);
});

test('missing group member tiers and invalid categories remain unknown', async (t) => {
    const { request, state } = await fixture(t);
    await request('roster', { monsters: [
        { id: 1, name: 'Gnomes', tier: 1 }, { id: 2, name: 'Gnomes' }
    ] });
    await request('monster-seen', { id: 1, name: 'Gnomes' });
    assert.equal((await state()).monsters[0].tier, null);
    for (const tier of [0, 4, '2', false]) {
        await request('monster-status', { statuses: [{ id: 2, alive: true, tier }] });
        assert.equal((await state()).roster[0].tier, null);
    }
    await request('monster-status', { statuses: [{ id: 2, alive: true, tier: 1 }] });
    assert.equal((await state()).monsters[0].tier, 1);
});

test('unknown strength stays absent, zero is valid, and vanilla slot limits do not drop unknowns', async (t) => {
    const { request, state } = await fixture(t);
    for (let id = 0; id < 5; id++) {
        await request('monster-seen', { id, name: `New Enemy ${id}` });
        await request('roster', { monsters: [{ id, name: `New Enemy ${id}`, unknown: true, strength: null }] });
    }
    assert.equal((await state()).monsters.length, 5);
    for (const monster of (await state()).monsters) assert.equal('strength' in monster, false);
    await request('monster-status', { statuses: [{ id: 0, alive: true, strengthForceMax: 6, strength: 0 }] });
    assert.equal((await state()).monsters[0].strength, 0);
    await request('monster-status', { statuses: [{ id: 0, alive: true, strengthForceMax: 6, strength: -5 }] });
    assert.equal((await state()).monsters[0].strength, undefined);
});

// Installed resources.assets: Grab Force - 1/2/2.5/3/4 Player amounts
// are 0.1/10/14/16/21; serialized PhysGrabber.forceMax is 6 (not 4).
test('raw grab thresholds convert to upgrade counts without null coercion', async (t) => {
    const { request, state } = await fixture(t);
    await request('monster-seen', { id: 1, name: 'Robes' });
    assert.equal((await state()).monsters[0].strength, undefined);
    for (const [raw, expected] of [[0, 0], [0.1, 0], [6, 0], [7.2, 1], [10, 4], [14, 7], [16, 9], [21, 13]]) {
        await request('roster', { monsters: [{ id: 1, name: 'Robes', unknown: true, strengthForceMax: 6, strength: raw }] });
        assert.equal((await state()).monsters[0].strength, expected);
    }
});

test('conversion requires measured forceMax and does not invent a numeric sentinel', async (t) => {
    const { request, state } = await fixture(t);
    await request('monster-seen', { id: 1, name: 'New Beast' });
    for (const [strength, strengthForceMax, expected] of [
        [10, undefined, undefined], [10, null, undefined], [10, 0, undefined],
        [10, -6, undefined], [10, '6', undefined], [null, 6, undefined],
        [0, undefined, undefined], [10, 4, 8], [10, 6, 4], [10, 10, 0],
        [16, 6, 9], [21, 6, 13],
        [7.200000286102295, 6, 1], [7.200000762939453, 6, 2],
        [100000, 6, 83329], [999999, 6, 833328],
        ['impossible', undefined, 'impossible']
    ]) {
        await request('monster-status', { statuses: [{ id: 1, alive: true, strength, strengthForceMax }] });
        assert.equal((await state()).monsters[0].strength, expected,
            `threshold=${strength}, forceMax=${strengthForceMax}`);
    }
});

test('early status metadata survives unrelated roster and partial updates', async (t) => {
    const { request, state } = await fixture(t);
    await request('monster-status', { statuses: [{ id: 1, alive: true, health: 30,
        maxHealth: 60, strengthForceMax: 6, strength: 10, unknown: true, playerClose: true, playerVeryClose: true }] });
    await request('roster', { monsters: [{ id: 2, name: 'Animal' }] });
    await request('monster-status', { statuses: [{ id: 1, alive: true, health: null }] });
    await request('monster-seen', { id: 1, name: 'New Beast' });
    const current = await state();
    const slot = current.roster.find((entry) => entry.id === 'enemy:1');
    assert.equal(slot.health, 30);
    assert.equal(slot.maxHealth, 60);
    assert.equal(slot.playerClose, true);
    assert.equal(slot.playerVeryClose, true);
    assert.equal(current.monsters[0].strength, 4);
});

test('known monsters use game strength and retain image, localization and partial HP', async (t) => {
    const { request, state, writes } = await fixture(t);
    await request('roster', { monsters: [{ id: 8, name: 'Animal', strengthForceMax: 6, strength: 10 }] });
    await request('monster-seen', { id: 8, name: 'Animal' });
    await request('monster-status', { statuses: [{ id: 8, alive: true, health: 50, maxHealth: 100 }] });
    await request('monster-status', { statuses: [{ id: 8, alive: true, health: 25 }] });
    const current = await state();
    assert.equal(current.monsters[0].unknown, undefined);
    assert.equal(current.monsters[0].strength, 4);
    assert.equal(current.monsters[0].image, 'assets/monsters/animal.webp');
    assert.equal(current.roster[0].maxHealth, 100);
    assert.equal(current.roster[0].health, 25);
    await request('monster-seen', { id: 9, name: '  New Beast / 未知  ' });
    await request('level', { level: 2, levelName: 'Swiftbroom Academy' });
    assert.ok(writes.at(-1).includes('Modded Museum Annex / 地下 [Животное,   New Beast / 未知  ]'));
    assert.match(writes.at(-1), /Уровень 2 Wizard\n$/);
});

for (const name of ['Animal', 'Peeper', 'New Beast', 'Gnomes']) {
    for (const order of eventOrders) {
        for (const provider of ['status', 'seen', 'roster']) {
            test(`strength ${name}, supplied by ${provider}: ${order.join(' -> ')}`, async (t) => {
                const { request, state } = await fixture(t);
                await request('level', { level: 20 });
                const send = (kind, metadata = {}) => {
                    const entry = { id: 42, name, ...metadata };
                    if (kind === 'status') return request('monster-status', { statuses: [{ ...entry, alive: true }] });
                    if (kind === 'roster') return request('roster', { monsters: [entry] });
                    return request('monster-seen', entry);
                };
                for (const kind of order) {
                    await send(kind, kind === provider ? { strengthForceMax: 6, strength: 21 } : {});
                    if (!order.slice(0, order.indexOf(kind) + 1).includes('seen')) {
                        assert.equal((await state()).monsters.length, 0);
                    }
                }
                const check = async (expected) => {
                    const current = await state();
                    assert.equal(current.monsters.length, 1);
                    assert.equal(current.monsters[0].strength, expected);
                    assert.equal(current.roster[0].strength, expected);
                    assert.equal(current.monsters[0].unknown, name === 'New Beast' ? true : undefined);
                };
                await check(13);
                for (const kind of order) {
                    for (const [raw, expected] of [[0, 0], [7.2, 1], ['impossible', 'impossible'], [null, undefined], [10, 4]]) {
                        await send(kind, { strengthForceMax: 6, strength: raw });
                        await check(expected);
                        for (const partial of order) await send(partial);
                        await check(expected);
                    }
                }
                await request('level', { level: 20 });
                await send('seen');
                assert.equal((await state()).monsters[0].strength, undefined);
            });
        }
    }
}

for (const order of eventOrders) {
    test(`group strength aggregation: ${order.join(' -> ')}`, async (t) => {
        const { request, state } = await fixture(t);
        const actions = {
            status: () => request('monster-status', { statuses: [{ id: 2, alive: true, strengthForceMax: 6, strength: 21 }] }),
            seen: () => request('monster-seen', { id: 1, name: 'Gnomes', strengthForceMax: 6, strength: 0 }),
            roster: () => request('roster', { monsters: [1, 2, 3, 4].map((id) => ({ id, name: 'Gnomes' })) })
        };
        for (const kind of order) await actions[kind]();
        const check = async (expected) => {
            const current = await state();
            assert.equal(current.monsters.length, 1);
            assert.equal(current.roster.length, 1);
            assert.equal(current.monsters[0].count, 4);
            assert.equal(current.monsters[0].strength, expected);
            assert.equal(current.roster[0].strength, expected);
        };
        await check(13);
        await request('monster-status', { statuses: [{ id: 3, alive: true, strength: 'impossible' }] });
        await check('impossible');
        await request('roster', { monsters: [{ id: 3, name: 'Gnomes', strength: null }] });
        await check(13);
        await request('monster-seen', { id: 2, name: 'Gnomes', strength: null });
        await check(0);
        await request('monster-status', { statuses: [{ id: 1, alive: true, strength: null }] });
        await check(undefined);
    });
}

test('every known alias uses supplied strength, never a name-based fallback', async (t) => {
    const { request, state } = await fixture(t);
    const context = vm.createContext({ require, module: { exports: {} }, process, console, __dirname });
    vm.runInContext(fs.readFileSync(`${__dirname}/server.js`, 'utf8') + '\nthis.names = Object.values(monsterConfig.aliases);', context);
    for (const name of new Set(context.names)) {
        await request('level', { level: 20 });
        await request('roster', { monsters: [{ id: 1, name }] });
        await request('monster-seen', { id: 1, name });
        assert.equal((await state()).monsters[0].strength, undefined, name);
        await request('monster-status', { statuses: [{ id: 1, alive: true, strengthForceMax: 6, strength: 20 }] });
        assert.equal((await state()).monsters[0].strength, 12, name);
        assert.equal((await state()).monsters[0].unknown, undefined, name);
    }
});

test('invalid explicit strength never becomes zero or impossible', async (t) => {
    const { request, state } = await fixture(t);
    await request('level', { level: 20 });
    await request('monster-seen', { id: 1, name: 'Animal' });
    for (const strength of [-1, '7', false, {}, 'Impossible']) {
        await request('monster-seen', { id: 1, name: 'Animal', strengthForceMax: 6, strength: 10 });
        await request('monster-status', { statuses: [{ id: 1, alive: true, strength }] });
        assert.equal((await state()).monsters[0].strength, undefined);
    }
});
