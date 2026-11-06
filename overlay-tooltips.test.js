const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');

const html = fs.readFileSync(`${__dirname}/overlay.html`, 'utf8');
const upgrades = require('./tests/localization/upgrade-names.json');
const monsters = require('./assets/monster-localization.json');
const bundledUpgrades = require('./assets/upgrade-localization.json');
function extract(name) {
    const start = html.indexOf(`        function ${name}(`);
    const end = html.indexOf('\n        }', start) + '\n        }'.length;
    assert(start >= 0 && end > start, name);
    return html.slice(start, end);
}
function boot(extra = {}) {
    const context = vm.createContext({ currentLanguage: 'ru', latestState: null, ...extra });
    const start = html.indexOf('        const interfaceText =');
    const end = html.indexOf('        function getText(', start);
    assert(start >= 0 && end > start);
    vm.runInContext(html.slice(start, end) + ['getText', 'getLocalizedHudName', 'getUpgradeName',
        'getMonsterName', 'updateMonsterTooltip', 'applyInterfaceLanguage', 'updateItemDestroyedAlert']
        .map(extract).join('\n'), context);
    return context;
}

test('all bundled upgrade names and UI fallbacks match verified Native fixtures', () => {
    assert.deepEqual(bundledUpgrades, upgrades);
    const ui = boot();
    for (const language of ['ru', 'en']) {
        ui.currentLanguage = language;
        for (const [key, labels] of Object.entries(upgrades)) {
            assert.equal(ui.getUpgradeName({ key, name: labels.en }), labels[language], `${key}/${language}`);
        }
    }
});

test('all official monster names render without changing their gameplay identity', () => {
    const translations = Object.fromEntries(Object.values(monsters).map(labels => [labels.en, labels]));
    translations.Gnomes = monsters['ENEMY.GNOME'];
    translations.Snatcher = monsters['ENEMY.SNATCHER'];
    const ui = boot({ latestState: { hudNames: { monsters: translations } } });
    for (const language of ['ru', 'en']) {
        ui.currentLanguage = language;
        for (const [name, labels] of Object.entries(translations)) {
            const monster = { id: 'stable-id', name };
            assert.equal(ui.getMonsterName(monster), labels[language]);
            assert.deepEqual(monster, { id: 'stable-id', name });
        }
    }
    ui.currentLanguage = 'ru';
    assert.equal(ui.getMonsterName({ name: 'Modded Enemy' }), 'Modded Enemy');
    assert.equal(ui.getMonsterName({ name: 'Banger', unknown: true }), 'Взрывник');
    for (const language of ['ru', 'en']) {
        ui.currentLanguage = language;
        for (const name of ['Snatcher', 'Pony']) {
            const monster = { id: 'unknown-icon', name, unknown: true };
            assert.equal(ui.getMonsterName(monster), monsters['ENEMY.SNATCHER'][language]);
            assert.deepEqual(monster, { id: 'unknown-icon', name, unknown: true });
        }
        assert.equal(ui.getMonsterName({ name: 'Modded Enemy', unknown: true }), 'Modded Enemy');
    }
    ui.currentLanguage = 'ru';
    ui.latestState = null;
    assert.equal(ui.getMonsterName({ name: 'Banger' }), 'Banger');
    assert.equal(ui.getMonsterName({ name: 'Snatcher', unknown: true }), 'Snatcher');
});

test('live translations update existing tooltip and upgrade labels without changing language', () => {
    const fields = new Map(['.monster-tooltip-name', '.monster-tooltip-health', '.monster-tooltip-strength']
        .map(key => [key, { textContent: '' }]));
    const tooltip = { querySelector: key => fields.get(key) };
    const square = { querySelector: () => tooltip };
    const img = {};
    const badge = { dataset: {}, querySelector: () => img };
    const upgradeTooltip = {};
    const label = {};
    const button = { classList: { toggle() {} }, setAttribute() {} };
    const ui = boot({
        latestState: { hudNames: { monsters: { Banger: { ru: 'Взрывник', en: 'Banger' } },
            upgrades: { extraJump: upgrades.extraJump } } },
        getMonsterHealthText: () => 'ХП: 50 / 100', getMonsterStrengthText: () => 'Сила: 3',
        document: { documentElement: {}, querySelectorAll: () => [] },
        overlayLanguageRuBtn: button, overlayLanguageEnBtn: button,
        upgradeDefinitions: [{ key: 'extraJump', name: 'Extra Jump' }],
        upgradeElements: new Map([['extraJump', { badge, tooltip: upgradeTooltip }]]),
        overlayUpgradeVisibilityToggles: new Map([['extraJump', { label }]])
    });
    const check = (language, monsterLabel, upgradeLabel) => {
        ui.applyInterfaceLanguage(language);
        ui.updateMonsterTooltip(square, { name: 'Banger' }, {});
        assert.equal(fields.get('.monster-tooltip-name').textContent, monsterLabel);
        assert.equal(fields.get('.monster-tooltip-health').textContent, 'ХП: 50 / 100');
        assert.equal(fields.get('.monster-tooltip-strength').textContent, 'Сила: 3');
        assert.equal(upgradeTooltip.textContent, upgradeLabel);
        assert.equal(badge.dataset.upgradeName, upgradeLabel);
        assert.equal(img.alt, upgradeLabel);
        assert.equal(label.textContent, upgradeLabel);
    };
    check('ru', 'Взрывник', 'Ещё один прыжок');
    ui.latestState = { hudNames: { monsters: { Banger: { ru: 'Новое имя', en: 'New name' } },
        upgrades: { extraJump: { ru: 'Новое улучшение', en: 'New upgrade' } } } };
    check('ru', 'Новое имя', 'Новое улучшение');
    check('en', 'New name', 'New upgrade');
    check('ru', 'Новое имя', 'Новое улучшение');
    ui.latestState.hudNames.monsters.Banger.ru = ' ';
    ui.latestState.hudNames.upgrades.extraJump.ru = null;
    check('ru', 'New name', 'New upgrade');
});

for (const language of ['ru', 'en']) {
    test(`Electron destruction alerts use 💥 and literal player/item text (${language})`, () => {
        const nodes = [];
        const classes = new Set();
        const ui = boot({
            currentLanguage: language, lastShownDestroyedItemAt: 0, itemDestroyedAlertHideTimer: 0,
            itemDestroyedAlert: { textContent: '', offsetWidth: 100, append: (...items) => nodes.push(...items),
                classList: { add: name => classes.add(name), remove: name => classes.delete(name) } },
            document: { createTextNode: textContent => ({ textContent }), createElement: () => ({}) },
            window: { clearTimeout() {}, setTimeout: () => 1 }, positionItemDestroyedAlert() {}
        });
        const entry = { player: '<Player>', item: '<Vase>', at: Date.now() };
        ui.updateItemDestroyedAlert({ itemDestroyedAlertsVisible: false, lastDestroyedItem: entry });
        assert.equal(nodes.length, 0);
        ui.updateItemDestroyedAlert({ itemDestroyedAlertsVisible: true, lastDestroyedItem: entry });
        assert.deepEqual(nodes.map(node => node.textContent), [
            '💥 <Player> ', language === 'ru' ? 'разрушил' : 'destroyed', ' <Vase>'
        ]);
        assert.ok(classes.has('is-visible'));
    });
}
