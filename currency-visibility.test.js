const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');

function loadServer() {
    const context = vm.createContext({ require, module: { exports: {} }, process, console, __dirname });
    vm.runInContext(fs.readFileSync(`${__dirname}/server.js`, 'utf8') + '\nthis.api = { setTabHidden, normalizeOverlayState, getNormalizedOverlayState };', context);
    return context.api;
}

function loadClient(initialState = null) {
    let stored = initialState ? JSON.stringify(initialState) : null;
    const context = vm.createContext({
        window: { location: { protocol: 'file:' }, addEventListener() {} },
        localStorage: { getItem: () => stored, setItem: (_, value) => { stored = value; } },
        console
    });
    vm.runInContext(fs.readFileSync(`${__dirname}/overlay-state.js`, 'utf8') + '\nthis.api = OverlayApp;', context);
    return context.api;
}

test('timestamp preference defaults on and persists with the interface language', () => {
    const client = loadClient();
    assert.equal(client.getState().saveTimestampsEnabled, true);
    client.setSaveTimestampsEnabled(false);
    client.setInterfaceLanguage('en');
    const restored = loadClient(client.getState());
    assert.equal(restored.getState().saveTimestampsEnabled, false);
    assert.equal(restored.getState().interfaceLanguage, 'en');
    restored.setSaveTimestampsEnabled(true);
    assert.equal(restored.getState().saveTimestampsEnabled, true);
    assert.equal(loadClient({ saveTimestampsEnabled: 'false' }).getState().saveTimestampsEnabled, true);
});

const plain = (value) => JSON.parse(JSON.stringify(value));
const position = (anchorY, anchorX = 'left') => ({ left: 20, top: 20, anchorX, anchorY });

for (const [name, overlayPosition, hidden] of [
    ['default null', null, true],
    ['default missing', undefined, true],
    ['top', position('top'), true],
    ['free', position(null), false],
    ['unanchored near top', { left: 0, top: 0 }, false],
    ['center', position('center'), false],
    ['bottom', position('bottom'), false]
]) {
    test(`currency visibility: ${name} position`, () => {
        const { shouldHideForGameUI: hide } = loadClient();
        assert.equal(hide({ currencyVisible: true, overlayPosition }), hidden);
        assert.equal(hide({ currencyVisible: false, overlayPosition }), false);
        for (const currencyVisible of [false, true]) {
            assert.equal(hide({ tabHidden: true, currencyVisible, overlayPosition }), true);
        }
    });
}

test('horizontal anchors do not affect currency suppression', () => {
    const { shouldHideForGameUI: hide } = loadClient();
    for (const anchorX of [null, 'left', 'center', 'right']) {
        for (const anchorY of [null, 'top', 'center', 'bottom']) {
            assert.equal(hide({ currencyVisible: true, overlayPosition: position(anchorY, anchorX) }), anchorY === 'top');
        }
    }
});

test('extended protocol keeps map and currency independent of legacy hidden', () => {
    const server = loadServer();
    const client = loadClient();
    for (const mapHidden of [false, true]) {
        for (const currencyVisible of [false, true]) {
            const result = server.setTabHidden({ hidden: true, mapHidden, currencyVisible });
            assert.equal(result.statusCode, 200);
            const state = server.getNormalizedOverlayState();
            assert.equal(state.tabHidden, mapHidden);
            assert.equal(state.currencyVisible, currencyVisible);
            assert.equal(client.shouldHideForGameUI({ ...state, overlayPosition: position('bottom') }), mapHidden);
            assert.equal(client.shouldHideForGameUI({ ...state, overlayPosition: null }), mapHidden || currencyVisible);
            assert.equal('currencyRect' in result.payload, false);
        }
    }
});

test('legacy flags still hide globally and clear currency visibility', () => {
    const server = loadServer();
    for (const key of ['hidden', 'tabHidden']) {
        for (const hidden of [true, false]) {
            server.setTabHidden({ mapHidden: false, currencyVisible: true });
            assert.equal(server.setTabHidden({ [key]: hidden }).statusCode, 200);
            const state = server.getNormalizedOverlayState();
            assert.equal(state.tabHidden, hidden);
            assert.equal(state.currencyVisible, false);
        }
    }
    const before = plain(server.getNormalizedOverlayState());
    assert.equal(server.setTabHidden({ hidden: 'false' }).statusCode, 422);
    assert.deepEqual(plain(server.getNormalizedOverlayState()), before);
    server.setTabHidden({ hidden: false, mapHidden: 'invalid', currencyVisible: true });
    assert.equal(server.getNormalizedOverlayState().currencyVisible, false);
});

test('normalization preserves flags and position but strips obsolete currencyRect', () => {
    const server = loadServer();
    const initial = {
        ...loadClient().getState(), tabHidden: false, currencyVisible: true,
        overlayPosition: position('bottom'), currencyRect: { x: 0, y: 0, width: 1, height: 1 }
    };
    const state = server.normalizeOverlayState(initial);
    assert.equal('currencyRect' in state, false);
    assert.equal('currencyRect' in initial, true);
    const client = loadClient(initial);
    client.setSquareSize(80);
    assert.equal(client.getState().currencyVisible, true);
    assert.equal(client.getState().tabHidden, false);
    assert.deepEqual(plain(client.getState().overlayPosition), initial.overlayPosition);
    assert.equal('currencyRect' in client.getState(), false);
    assert.equal(client.shouldHideForGameUI(client.getState()), false);
});

const html = fs.readFileSync(`${__dirname}/overlay.html`, 'utf8');
function inlineFunction(name) {
    const start = html.indexOf(`        function ${name}(`);
    const end = html.indexOf('\n        function ', start + 1);
    assert.ok(start >= 0 && end > start);
    return html.slice(start, end);
}

test('drag visibility uses the current overlay candidate, not the saved anchor or controls drag', () => {
    let hidden = false;
    let controlsEnabled = true;
    const element = { style: { setProperty() {} } };
    const context = vm.createContext({
        OverlayApp: loadClient(),
        latestState: { gameplayVisible: true, currencyVisible: true, overlayPosition: position('bottom') },
        activeDrag: { targetName: 'overlay', pointerId: 1, element, startLeft: 0, startTop: 0, startX: 0, startY: 0 },
        candidate: position('top'),
        overlayContainer: {
            hasAttribute: () => hidden,
            toggleAttribute: (_, value) => { hidden = value; }
        },
        overlayHoverControls: { classList: { toggle: (_, value) => { controlsEnabled = value; } } },
        updateSafeZoneGuides() {}
    });
    vm.runInContext('function snapPosition() { return candidate; }\n' + inlineFunction('moveOverlayDrag') + inlineFunction('checkGameUIVisibility'), context);
    const event = { pointerId: 1, clientX: 0, clientY: 0, preventDefault() {} };
    context.moveOverlayDrag(event);
    assert.equal(hidden, true);
    assert.equal(controlsEnabled, false);
    context.candidate = position(null);
    context.moveOverlayDrag(event);
    assert.equal(hidden, false);
    assert.equal(controlsEnabled, true);
    context.latestState.overlayPosition = position('top');
    context.checkGameUIVisibility();
    assert.equal(hidden, false);
    context.activeDrag.targetName = 'controls';
    context.checkGameUIVisibility();
    assert.equal(hidden, true);
    context.latestState.overlayPosition = position('bottom');
    context.latestState.tabHidden = true;
    context.checkGameUIVisibility();
    assert.equal(hidden, true);
    context.activeDrag = null;
    context.latestState.tabHidden = false;
    context.checkGameUIVisibility();
    assert.equal(hidden, false);
});

test('inline UI script parses and has no currency geometry machinery', () => {
    const scripts = [...html.matchAll(/<script(?:\s[^>]*)?>([\s\S]*?)<\/script>/g)];
    assert.ok(scripts.some((match) => match[1].includes('function render(')));
    for (const match of scripts) new vm.Script(match[1]);
    assert.doesNotMatch(html, /currencyRect|normalizeCurrencyRect|hudGeometry|hudRects|MutationObserver|ResizeObserver|IntersectionObserver/);
});
