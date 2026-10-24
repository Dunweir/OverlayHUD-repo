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
    for (const changes of [{ mapValue: 0 }, { lostValue: 0 }, { gameplayVisible: false }, { level: 2 }, { squareSize: 90 }, { roster: [{ id: 1 }] }, { interfaceLanguage: "en" }]) {
        assert.deepEqual(render(base, changes).calls, ["full"]);
    }
    assert.deepEqual(render({ ...base, lostValue: 0 }, { lostValue: 1 }).calls, ["full"]);
    assert.deepEqual(render(null, base).calls, ["full"]);
});
