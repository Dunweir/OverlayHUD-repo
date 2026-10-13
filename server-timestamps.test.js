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
    api.postState = (state) => new Promise((resolve, reject) => {
        const request = Readable.from([JSON.stringify({ state })]);
        request.method = 'POST';
        request.url = '/api/state';
        api.server.emit('request', request, {
            writeHead(code) { if (code !== 200) reject(new Error(`HTTP ${code}`)); },
            end(body) { resolve(JSON.parse(body)); }
        });
    });
    api.patchState = (patch) => api.postState({ ...api.getNormalizedOverlayState(), ...patch });
    return { api, writes, directories };
}

const translations = {
    'Animal': 'Зверь', 'Apex Predator': 'Утка', 'Banger': 'Черепа',
    'Bella': 'Белла', 'Birthday Boy': 'Именинник', 'Bowtie': 'Бабочка',
    'Chef': 'Повар', 'Cleanup Crew': 'Уборщик', 'Clown': 'Клоун',
    'Elsa': 'Эльза', 'Gambit': 'Гамбит', 'Gnomes': 'Гномы',
    'Headgrab': 'Хэдграб', 'Headman': 'Хэдмен', 'Heart Hugger': 'Сердцеед',
    'Hidden': 'Невидимка', 'Huntsman': 'Охотник', 'Loom': 'Лум',
    'Mentalist': 'Менталист', 'Oogly': 'Угли', 'Peeper': 'Глаз',
    'Reaper': 'Жнец', 'Robe': 'Роба', 'Rugrat': 'Карапуз',
    'Shadow Child': 'Теневой ребёнок', 'Spewer': 'Плевун',
    'Tick': 'Клещ', 'Trudge': 'Топтун', 'Upscream': 'Крикун'
};

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

test('all exact Russian translations and English canonical names', () => {
    const { api } = loadServer();
    for (const [name, translation] of Object.entries(translations)) {
        assert.equal(api.formatMonsterTimestampList([{ name }], 'ru')[0], translation);
        assert.equal(api.formatMonsterTimestampList([{ name }], 'en')[0], name);
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
        'Гномы x4', 'Утка x2', 'Теневой ребёнок', 'Повар', 'Клоун',
        'Зверь x2', 'Черепа', 'Бабочка', 'Менталист', 'Глаз', 'Плевун', 'Крикун', 'Unknown'
    ]);
});

for (const [initial, next, heading, monster, nextHeading] of [
    ['ru', 'en', 'Уровень', 'Зверь', 'Level'],
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
    assert.match(writes.at(-1).text, /Уровень 1 Manor \[Гномы x4\]\n$/);
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
