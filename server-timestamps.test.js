const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const { Readable } = require('node:stream');

function loadServer() {
    const writes = [];
    const directories = [];
    const context = vm.createContext({
        require: (name) => name === 'fs' ? {
            mkdirSync: (...args) => directories.push(args),
            writeFileSync: (file, text) => writes.push({ file, text })
        } : require(name),
        module: { exports: {} }, process, console, __dirname
    });
    vm.runInContext(fs.readFileSync(`${__dirname}/server.js`, 'utf8') + `
        this.api = { server, normalizeOverlayState, setGameLevel, setGameplayVisibility,
            stopOverlayServer, formatMonsterTimestampList, writeTimestampSession,
            getNormalizedOverlayState };
    `, context);
    const api = context.api;
    api.post = (url, payload) => new Promise((resolve) => {
        const request = Readable.from([JSON.stringify(payload)]);
        request.method = 'POST';
        request.url = url;
        let status;
        api.server.emit('request', request, {
            writeHead(code) { status = code; },
            end(body) { resolve({ status, body: JSON.parse(body) }); }
        });
    });
    api.postState = async (state) => {
        const result = await api.post('/api/state', { state });
        assert.equal(result.status, 200);
        return result.body;
    };
    api.patchState = (patch) => api.postState({ ...api.getNormalizedOverlayState(), ...patch });
    return { api, writes, directories };
}

const translations = {
    'Animal': 'Животное', 'Apex Predator': 'Высший хищник', 'Banger': 'Взрывник',
    'Bella': 'Динь', 'Birthday Boy': 'Именинник', 'Bowtie': 'Бабочкин',
    'Chef': 'Повар', 'Cleanup Crew': 'Уборочная бригада', 'Clown': 'Шут',
    'Elsa': 'Эльза', 'Gambit': 'Рулетка', 'Gnomes': 'Гном',
    'Headgrab': 'Главохват', 'Headman': 'Главарь', 'Heart Hugger': 'Сердцежим',
    'Hidden': 'Неизвестный', 'Huntsman': 'Охотник', 'Loom': 'Иллюзия',
    'Mentalist': 'Телепат', 'Oogly': 'Чердачник', 'Peeper': 'Смотритель',
    'Reaper': 'Пугалка', 'Robe': 'Мантия', 'Rugrat': 'Спиногрыз',
    'Shadow Child': 'Дитя Тени', 'Spewer': 'Блевотник',
    'Tick': 'Клещ', 'Trudge': 'Шагатель', 'Upscream': 'Крикун', 'Snatcher': 'Пони'
};

test('live game tables update both languages without changing identity or counts', async () => {
    const { api, writes } = loadServer();
    api.setGameLevel(1, 'Manor');
    await api.patchState({ monsters: [{ name: 'Gnomes', count: 4 }] });
    const result = await api.post('/api/monster-localization', { names: {
        'ENEMY.GNOME': { en: 'Updated Gnome', ru: 'Новый гном' },
        'ENEMY.SNATCHER': { en: 'Updated Pony', ru: 'Новая пони' }
    } });
    assert.equal(result.status, 200);
    assert.equal(api.formatMonsterTimestampList([{ name: 'Gnomes', count: 4 }], 'en')[0], 'Updated Gnome x4');
    assert.equal(api.formatMonsterTimestampList([{ name: 'Snatcher', unknown: true }], 'ru')[0], 'Новая пони');
    assert.equal(api.formatMonsterTimestampList([{ name: 'Animal' }], 'ru')[0], 'Животное');
    api.setGameLevel(2, 'Manor');
    assert.match(writes.at(-1).text, /\[Новый гном x4\]/);
});

test('invalid localization is rejected atomically and missing translations fall back', async () => {
    const { api } = loadServer();
    assert.equal((await api.post('/api/monster-localization', { names: {
        'ENEMY.ANIMAL': { en: 'Updated Animal' }
    } })).status, 200);
    assert.equal(api.formatMonsterTimestampList([{ name: 'Animal' }], 'ru')[0], 'Животное');
    for (const names of [null, [], { 'ENEMY.ANIMAL': { en: 'Bad\nName' } },
        { 'ENEMY.ANIMAL': { ru: 'Changed' }, invalid: { en: 'Invalid' } }]) {
        assert.equal((await api.post('/api/monster-localization', { names })).status, 422);
        assert.equal(api.formatMonsterTimestampList([{ name: 'Animal' }], 'en')[0], 'Updated Animal');
        assert.equal(api.formatMonsterTimestampList([{ name: 'Animal' }], 'ru')[0], 'Животное');
    }
});

test('timestamp setting defaults to true and normalizes only booleans', async () => {
    const { api } = loadServer();
    assert.equal(api.normalizeOverlayState({}).saveTimestampsEnabled, true);
    for (const value of [true, false, null, undefined, 0, 1, 'false', {}, []]) {
        const expected = typeof value === 'boolean' ? value : true;
        assert.equal(api.normalizeOverlayState({ saveTimestampsEnabled: value }).saveTimestampsEnabled, expected);
        await api.postState({ saveTimestampsEnabled: value });
        assert.equal(api.getNormalizedOverlayState().saveTimestampsEnabled, expected);
    }
});

test('all official Russian and English names from game bundles', () => {
    const { api } = loadServer();
    for (const [name, translation] of Object.entries(translations)) {
        assert.equal(api.formatMonsterTimestampList([{ name }], 'ru')[0], translation);
        assert.equal(api.formatMonsterTimestampList([{ name }], 'en')[0],
            name === 'Gnomes' ? 'Gnome' : name === 'Snatcher' ? 'Pony' : name);
    }
});

test('timestamp aliases combine counts and preserve unknown names', () => {
    const { api } = loadServer();
    const monsters = [
        { name: 'Gnome' }, { name: 'gnomes', count: 3 },
        { name: 'Apex Predators' }, { name: 'Apex Predator' },
        { name: 'Shadow Children' }, { name: 'Chef Frog' },
        { name: 'Clown Beamer' }, { name: 'Animals', count: 2 },
        { name: 'Bangers' }, { name: 'Bowties' }, { name: 'Mentalists' },
        { name: 'Peepers' }, { name: 'Spewers' }, { name: 'Upscreams' },
        { name: 'Unknown' }, { name: '' }
    ];
    assert.deepEqual(Array.from(api.formatMonsterTimestampList(monsters, 'ru')), [
        'Гном x4', 'Высший хищник x2', 'Дитя Тени', 'Повар', 'Шут',
        'Животное x2', 'Взрывник', 'Бабочкин', 'Телепат', 'Смотритель', 'Блевотник', 'Крикун', 'Unknown'
    ]);
});

for (const [initial, next, heading, monster, nextHeading] of [
    ['ru', 'en', 'Уровень', 'Животное', 'Level'],
    ['en', 'ru', 'Level', 'Animal', 'Уровень']
]) {
    test(`active line retains ${initial} after switching to ${next}`, async () => {
        const { api, writes } = loadServer();
        await api.postState({ interfaceLanguage: initial });
        api.setGameLevel(1, 'Swiftbroom Academy');
        await api.patchState({ interfaceLanguage: next, monsters: [{ name: 'Animal' }] });
        api.setGameLevel(2, 'MacJannek Station');
        const lines = writes.at(-1).text.trim().split('\n');
        assert.match(lines[0], new RegExp(`^\\d{2}:\\d{2}:\\d{2} ${heading} 1 Wizard \\[${monster}\\]$`));
        assert.match(lines[1], new RegExp(`${nextHeading} 2 Arctic$`));
        api.setGameplayVisibility(false);
        await api.stopOverlayServer();
        assert.equal(writes.at(-1).text.trim().split('\n').length, 2);
    });
}

test('unknown timestamp names bypass localization and plural inference in both languages', async () => {
    const { api, writes } = loadServer();
    for (const language of ['ru', 'en']) {
        assert.deepEqual(Array.from(api.formatMonsterTimestampList([
            { name: 'Robes' }, { name: 'Animal', unknown: true },
            { name: '  Modded / 未知  ', unknown: true }
        ], language)), ['Robes', 'Animal', '  Modded / 未知  ']);
        await api.postState({ interfaceLanguage: language });
        api.setGameLevel(1, '  Modded Museum / 未知  ');
        assert.ok(writes.at(-1).text.includes('1   Modded Museum / 未知  \n'));
        api.setGameLevel(2, 'Museum of Human Arts');
        assert.match(writes.at(-1).text, /2 Museum\n$/);
    }
});

test('timestamp locations omit the game Level prefix in both languages', async () => {
    for (const language of ['ru', 'en']) {
        const { api, writes } = loadServer();
        await api.postState({ interfaceLanguage: language });
        api.setGameLevel(1, 'Level - New Map');
        assert.match(writes.at(-1).text, /1 New Map\n$/);
        assert.doesNotMatch(writes.at(-1).text, /Level -/);
        api.setGameLevel(2, 'Level - Museum of Human Arts');
        assert.match(writes.at(-1).text, /2 Museum\n$/);
        api.setGameLevel(3, 'Mod Level - Annex');
        assert.match(writes.at(-1).text, /3 Mod Level - Annex\n$/);
    }
});

test('line-of-sight detection remains enabled by default', () => {
    const plugin = fs.readFileSync(`${__dirname}/overlay-hud/Plugin.cs`, 'utf8');
    assert.match(plugin, /Config\.Bind\("Detection", "RequireLineOfSight", true,/);
});

test('disabled from startup creates no files, including on shutdown', async () => {
    const { api, writes, directories } = loadServer();
    await api.postState({ saveTimestampsEnabled: false });
    api.setGameLevel(1, 'Manor');
    api.setGameLevel(2, 'Museum');
    api.setGameplayVisibility(false);
    api.writeTimestampSession();
    await api.stopOverlayServer();
    assert.equal(api.getNormalizedOverlayState().saveTimestampsEnabled, false);
    assert.equal(writes.length, 0);
    assert.equal(directories.length, 0);
});

test('disable finalizes once, stops writes and resumes only on the next level', async () => {
    const { api, writes, directories } = loadServer();
    api.setGameLevel(1, 'Headman Manor');
    await api.patchState({ monsters: [{ name: 'Gnome', count: 4 }] });
    await api.patchState({ saveTimestampsEnabled: false });
    assert.match(writes.at(-1).text, /Уровень 1 Manor \[Гном x4\]\n$/);
    const saved = writes.at(-1);
    const count = writes.length;
    await api.patchState({ monsters: [{ name: 'Animal' }] });
    api.setGameLevel(2, 'Museum');
    api.setGameplayVisibility(false);
    api.writeTimestampSession();
    await api.stopOverlayServer();
    assert.equal(writes.length, count);
    assert.equal(directories.length, count);
    await api.patchState({ saveTimestampsEnabled: true });
    api.setGameplayVisibility(false);
    await api.stopOverlayServer();
    assert.equal(writes.length, count);
    api.setGameLevel(3, 'Museum of Human Arts');
    assert.equal(writes.at(-1).file, saved.file);
    assert.ok(writes.at(-1).text.startsWith(saved.text));
    assert.match(writes.at(-1).text, /Уровень 3 Museum\n$/);
    assert.doesNotMatch(writes.at(-1).text, /Уровень 2/);
});
