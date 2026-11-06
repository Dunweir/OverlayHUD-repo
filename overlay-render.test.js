const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const vm = require("node:vm");

const html = fs.readFileSync(`${__dirname}/overlay.html`, "utf8");
const start = html.indexOf("function render(state) {");
const end = html.indexOf("const keepInlineUntilUpgradesHidden", start);
assert(start >= 0 && end > start);
// Stop at the full-render boundary; fast paths must not reach it.
const renderPrefix = html.slice(start, end) + 'calls.push("full"); }';
function render(previous, changes) {
    const calls = [];
    const state = { ...previous, ...changes };
    const context = vm.createContext({
        latestState: previous, calls, window: {},
        updateCurrencyValues: () => calls.push("values"),
        applySavedPositionsWhenLayoutChanges: () => calls.push("position"),
        updateItemDestroyedAlert: () => calls.push("alert"),
        checkGameUIVisibility: () => calls.push("visibility")
    });
    vm.runInContext(renderPrefix, context);
    context.render(state);
    return { calls, latest: context.latestState, state };
}
const base = { gameplayVisible: true, mapValue: 1000, lostValue: 20, level: 1, roster: [], lastDestroyedItem: null };

test('authoritative HP updates show successive and lethal damage, but not snapshots or healing', () => {
    const start = html.indexOf('function updateMonsterDamageFeedback(');
    const end = html.indexOf('function updateMonsterTooltip(', start);
    const pops = [];
    const square = { dataset: {}, classList: { add() {}, remove() {} }, appendChild: node => pops.push(node.textContent) };
    const context = vm.createContext({ document: { createElement: () => ({ remove() {} }) }, setTimeout() {} });
    vm.runInContext(html.slice(start, end), context);
    for (const health of [100, 70, 40, 40, 0, 100]) context.updateMonsterDamageFeedback(square, { health }, 'enemy-7');
    assert.deepEqual(pops, ['-30 HP', '-30 HP', '-40 HP']);
});

test('local monster entries do not invent strength and UI distinguishes unknown, zero and impossible', () => {
    const source = fs.readFileSync(`${__dirname}/overlay-state.js`, 'utf8');
    const start = source.indexOf('    function createMonsterEntry(');
    const end = source.indexOf('    function trimMonstersForLevel(', start);
    const textStart = html.indexOf('        function getMonsterStrengthText(');
    const textEnd = html.indexOf('\n        }', textStart) + '\n        }'.length;
    const context = vm.createContext({
        getMonsterImage: () => 'image', getMonsterFileName: () => 'animal',
        getMonsterCount: () => null,
        getText: (key) => ({ strengthUnknown: '?', strengthImpossible: 'impossible', strengthValue: (value) => String(value) })[key]
    });
    vm.runInContext(source.slice(start, end) + html.slice(textStart, textEnd), context);
    for (const name of ['Animal', 'Peeper', 'New Beast']) {
        assert.equal(context.createMonsterEntry(name, 1).strength, undefined);
    }
    for (const [strength, text] of [[undefined, '?'], [null, '?'], [0, '0'], [13, '13'], ['impossible', 'impossible']]) {
        assert.equal(context.getMonsterStrengthText({ strength }), text);
    }
    assert.doesNotMatch(source, /monsterConfig\.strength/);
});

test("roster and fallback render in game tier order with missing categories last", () => {
    const start = html.indexOf("function compareMonsterTiers(");
    const end = html.indexOf("function updateMonsterSquare(", start);
    const renderStart = html.indexOf("            if (state.rosterPending) {");
    const renderEnd = html.indexOf("            updateTopbarLayout({", renderStart);
    assert(start >= 0 && end > start && renderStart >= 0 && renderEnd > renderStart);
    const monsters = [
        { id: "new", sourceId: "new", level: 1, tier: 3, unknown: true },
        { id: "medium", sourceId: "medium", level: 2, tier: 2 },
        { id: "missing", sourceId: "missing", level: 1, tier: null },
        { id: "easy", sourceId: "easy", level: 3, tier: 1 }
    ];
    for (const withRoster of [true, false]) {
        const rendered = [];
        const context = vm.createContext({
            state: { monsters, strength: 0, level: 1 },
            roster: withRoster ? monsters.map((monster) => ({ ...monster })) : [],
            monstersSquares: { children: [] },
            ensureMonsterSquares: () => {},
            updateMonsterSquare: (square, level, monster) => rendered.push(monster.id),
            OverlayApp: { getCountsForLevel: () => ({ level1: 0, level2: 0, level3: 0 }) }
        });
        vm.runInContext(html.slice(start, end) + html.slice(renderStart, renderEnd), context);
        assert.deepEqual(rendered, ["easy", "medium", "new", "missing"]);
        assert.deepEqual(monsters.map((monster) => monster.id), ["new", "medium", "missing", "easy"]);
    }
});

test("game tier controls borders independently of name and layout level", () => {
    const start = html.indexOf("function updateMonsterSquare(");
    const end = html.indexOf("            setMonsterProximityState(", start);
    assert(start >= 0 && end > start);
    const context = vm.createContext({});
    vm.runInContext(html.slice(start, end) + "}", context);
    const classes = new Set(["level-3"]);
    const square = { classList: {
        remove: (...names) => names.forEach((name) => classes.delete(name)),
        add: (name) => classes.add(name)
    } };
    for (const [level, monster, expected] of [
        [1, { unknown: true, tier: 3 }, "level-3"],
        [3, { tier: 2 }, "level-2"],
        [2, { unknown: true, tier: 1 }, "level-1"],
        [1, { unknown: true }, "level-unknown"],
        [2, {}, "level-unknown"],
        [3, { tier: null }, "level-unknown"],
        [1, { tier: 0 }, "level-unknown"],
        [2, { tier: 4 }, "level-unknown"],
        [3, { tier: "3" }, "level-unknown"],
        [1, null, "level-1"]
    ]) {
        context.updateMonsterSquare(square, level, monster, 0);
        assert.deepEqual([...classes], [expected]);
    }
    // Unseen roster slots must use the same game tier as sorting, including Pony.
    for (const [tier, expected] of [[3, "level-3"], [2, "level-2"], [1, "level-1"], [null, "level-unknown"], [undefined, "level-unknown"]]) {
        context.updateMonsterSquare(square, 1, undefined, 0, { tier });
        assert.deepEqual([...classes], [expected]);
    }
    const css = fs.readFileSync(`${__dirname}/overlay.css`, "utf8");
    assert.match(css, /\.monster-square\.level-unknown\s*\{[^}]*border:[^}]*#dfb85b;[^}]*--glow-color: #dfb85b;/);
});

test("position cache tracks currency width and zero crossings rather than amount", () => {
    const extract = (name) => {
        const start = html.indexOf(`function ${name}(`);
        const end = html.indexOf("\n        }", start) + "\n        }".length;
        assert(start >= 0 && end > start);
        return html.slice(start, end);
    };
    const context = vm.createContext({ window: { innerWidth: 1920, innerHeight: 1080 } });
    vm.runInContext(extract("getCurrencyWidthCh") + "\n" + extract("getPositionLayoutKey"), context);
    const key = context.getPositionLayoutKey;
    assert.equal(key(base), key({ ...base, mapValue: 1100, lostValue: 35 }));
    assert.notEqual(key(base), key({ ...base, mapValue: 999 }));
    assert.notEqual(key(base), key({ ...base, lostValue: 100 }));
    assert.notEqual(key({ ...base, lostValue: 0 }), key({ ...base, lostValue: 1 }));
});

test("notification and visibility updates skip full HUD render", () => {
    for (const changes of [{ lastDestroyedItem: { player: "A", item: "Vase", at: 1 } }, { tabHidden: true }, { currencyVisible: true }]) {
        const result = render(base, changes);
        assert.deepEqual(result.calls, ["alert", "visibility"]);
        assert.equal(result.latest, result.state);
    }
});
test("value updates only refresh counters and position", () => {
    for (const changes of [{ mapValue: 900 }, { lostValue: 120 }, { mapValue: 900, lostValue: 120, lastDestroyedItem: { at: 2 } }]) {
        assert.deepEqual(render(base, changes).calls, ["values", "position", "alert", "visibility"]);
    }
});
test("zero crossings, gameplay activation and layout changes retain full rendering", () => {
    for (const changes of [{ mapValue: 0 }, { lostValue: 0 }, { gameplayVisible: false }, { level: 2 }, { squareSize: 90 }, { roster: [{ id: 1 }] }, { interfaceLanguage: "en" }, { hudNames: { monsters: { Banger: { ru: "Взрывник" } } } }]) {
        assert.deepEqual(render(base, changes).calls, ["full"]);
    }
    assert.deepEqual(render({ ...base, lostValue: 0 }, { lostValue: 1 }).calls, ["full"]);
    assert.deepEqual(render(null, base).calls, ["full"]);
});
