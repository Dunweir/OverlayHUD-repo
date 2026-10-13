const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");
const { EventEmitter } = require("node:events");

const source = fs.readFileSync(path.join(__dirname, "main.js"), "utf8");
const display = (id, x = 0, y = 0) => ({ id, label: `Display ${id}`, bounds: { x, y, width: 1920, height: 1080 } });
const flush = () => new Promise((resolve) => setImmediate(resolve));

async function boot(config = {}, args = []) {
    const state = {
        config: JSON.stringify(config), displays: [display(10), display(20, -1920)], primary: 10,
        errors: [], writes: [], captures: 0, dialogs: 0, calls: [],
        timers: new Map(), nextTimer: 1, boundsWrites: 0,
        saveResult: { canceled: false, filePath: "overlay.png" }
    };
    const app = Object.assign(new EventEmitter(), {
        isPackaged: false,
        disableHardwareAcceleration() { state.calls.push("disableGPU"); },
        requestSingleInstanceLock: () => true,
        whenReady() { state.calls.push("ready"); return Promise.resolve(); },
        commandLine: { hasSwitch: (name) => args.some((arg) => arg === `--${name}` || arg.startsWith(`--${name}=`)) },
        relaunch(options) { state.relaunch = options; state.calls.push("relaunch"); },
        quit() { state.calls.push("quit"); }
    });
    class Window extends EventEmitter {
        constructor(options) {
            super();
            state.window = this;
            this.options = options;
            this.bounds = options;
            this.visible = true;
            this.destroyed = false;
            this.webContents = Object.assign(new EventEmitter(), {
                setZoomFactor() {}, getZoomFactor: () => config.zoomFactor || 1,
                setWindowOpenHandler() {}, isLoading: () => Boolean(state.loading),
                executeJavaScript: () => state.rectPromise || Promise.resolve(null),
                async capturePage() {
                    state.captures++;
                    if (state.captureError) throw new Error("capture failure");
                    if (state.capturePromise) return state.capturePromise;
                    return image();
                }
            });
        }
        isDestroyed() { return this.destroyed; }
        isVisible() { return this.visible; }
        setBounds(bounds) {
            state.boundsWrites++;
            this.bounds = { ...bounds };
            if (state.distortBounds) this.bounds.width *= 1.5;
            this.emit("move"); this.emit("resize");
        }
        getBounds() { return this.bounds; }
        getContentBounds() { return this.bounds; }
        setResizable(value) { state.resizable = value; }
        hookWindowMessage(message, callback) { state.dpiMessage = callback; }
        setAlwaysOnTop() {}
        setIgnoreMouseEvents(value) { state.passthrough = value; }
        setFocusable() {}
        setOpacity() {}
        loadURL() {}
        showInactive() { this.visible = true; }
        hide() { this.visible = false; }
    }
    function image() {
        return {
            isEmpty: () => Boolean(state.empty),
            toPNG() {
                if (state.encodeError) throw new Error("encoding failure");
                return state.emptyPNG ? Buffer.alloc(0) : Buffer.from("mock PNG with alpha");
            }
        };
    }
    const screen = Object.assign(new EventEmitter(), {
        getAllDisplays: () => state.displays,
        getPrimaryDisplay: () => state.displays.find((d) => d.id === state.primary),
        getCursorScreenPoint: () => ({ x: 0, y: 0 })
    });
    const electron = {
        app, screen, BrowserWindow: Window,
        Menu: { buildFromTemplate: (template) => template },
        Tray: class extends EventEmitter {
            setContextMenu(menu) { state.menu = menu; }
            setToolTip() {}
        },
        nativeImage: { createFromPath: () => ({ resize() { return this; } }) },
        globalShortcut: { register() {}, unregisterAll() {} }, shell: {},
        dialog: {
            showErrorBox(title, message) { state.errors.push(message); },
            showMessageBoxSync(options) { state.confirmOptions = options; return state.confirmResult ?? 0; },
            async showSaveDialog(options) {
                state.dialogs++;
                state.dialogOptions = options;
                if (state.dialogError) throw new Error("dialog failure");
                return state.savePromise || state.saveResult;
            }
        }
    };
    const fakeFS = {
        readFileSync() {
            if (state.readError) throw state.readError;
            return state.config;
        },
        writeFileSync(file, content) {
            if (state.configWriteError) throw new Error("read-only config");
            state.config = content;
        },
        promises: { async writeFile(file, bytes) {
            if (state.writeError) throw new Error("disk full");
            state.writes.push({ file, bytes });
        } }
    };
    const context = vm.createContext({
        require(name) {
            if (name === "electron") return electron;
            if (name === "fs") return fakeFS;
            if (name === "path") return path;
            if (name === "./game-lifetime") return {
                startGameLifetimeWatcher(options) {
                    state.lifetimeWatcher = options;
                    return () => { state.lifetimeStopped = true; };
                }
            };
            if (name.endsWith("server.js")) return { async startOverlayServer() {}, stopOverlayServer() {} };
            throw new Error(`Unexpected dependency: ${name}`);
        },
        __dirname, process: { ...process, argv: ["electron", "app", ...args] }, console: { error() {}, warn() {} },
        setInterval: () => 1, clearInterval() {},
        setTimeout(callback) { const id = state.nextTimer++; state.timers.set(id, callback); return id; },
        clearTimeout(id) { state.timers.delete(id); }
    });
    vm.runInContext(source + `\nthis.api = {
        getTargetDisplay, selectDisplay, saveOverlayScreenshot, refreshOverlayContentBounds, screenshotFileName,
        seedBounds() {
            overlayContentBounds = overlayInteractiveBounds = overlayHotspotBounds = { left: 1 };
            overlayDragBounds = [{ left: 1 }]; overlayDragging = true; dragHoldUntil = 999999;
            mousePassthrough = false;
        },
        bounds: () => [overlayContentBounds, overlayInteractiveBounds, overlayHotspotBounds,
            overlayDragBounds.length, overlayDragging, dragHoldUntil],
        busy: () => screenshotInProgress
    };`, context);
    await flush();
    function runTimers() {
        let count = 0;
        while (state.timers.size) {
            assert(++count < 30, "timers must converge");
            const [id, callback] = state.timers.entries().next().value;
            state.timers.delete(id);
            callback();
        }
    }
    return { state, api: context.api, screen, app, image, runTimers };
}

test("tray changes the shared overlay language and timestamp preference", async () => {
    const { state } = await boot();
    const language = () => state.menu.find((item) => item.label === "Язык / Language").submenu;
    const timestamps = () => state.menu.find((item) => item.label === "Прочее").submenu.find((item) => item.label === "Сохранять таймкоды");
    assert.equal(language()[0].checked, true);
    assert.equal(timestamps().checked, true);
    const preferences = { interfaceLanguage: "ru", saveTimestampsEnabled: true };
    state.window.webContents.executeJavaScript = async (script) => {
        return vm.runInNewContext(script, { OverlayApp: {
            setInterfaceLanguage(value) { preferences.interfaceLanguage = value; },
            setSaveTimestampsEnabled(value) { preferences.saveTimestampsEnabled = value; },
            getState: () => preferences
        } });
    };
    await language()[1].click();
    assert.equal(preferences.interfaceLanguage, "en");
    assert.equal(language()[1].checked, true);
    await timestamps().click({ checked: false });
    assert.equal(preferences.saveTimestampsEnabled, false);
    assert.equal(timestamps().checked, false);
    assert.deepEqual(state.errors, []);
});

test("tray follows preferences changed in the overlay panel", async () => {
    const { state, api } = await boot();
    state.rectPromise = Promise.resolve({ preferences: { interfaceLanguage: "en", saveTimestampsEnabled: false } });
    await api.refreshOverlayContentBounds();
    assert.equal(state.menu.find((item) => item.label === "Язык / Language").submenu[1].checked, true);
    assert.equal(state.menu.find((item) => item.label === "Прочее").submenu.find((item) => item.label === "Сохранять таймкоды").checked, false);
});

test("tray preference changes fail safely while the overlay is loading", async () => {
    const { state } = await boot();
    state.loading = true;
    await state.menu.find((item) => item.label === "Язык / Language").submenu[1].click();
    assert.equal(state.errors.length, 1);
    assert.equal(state.menu.find((item) => item.label === "Язык / Language").submenu[0].checked, true);
});

const monitorMenu = (state) => state.menu.find((item) => item.label === "Монитор");

test("hardware acceleration is default and only boolean false disables it before ready", async () => {
    for (const value of [undefined, false, "false", true]) {
        const { state } = await boot({ hardwareAcceleration: value });
        assert.deepEqual(state.calls, value === false ? ["disableGPU", "ready"] : ["ready"]);
        assert.equal(state.window.options.transparent, true);
        assert.equal(state.window.options.backgroundColor, "#00000000");
    }
});

const accelerationMenu = (state) => state.menu.find((item) => item.label.startsWith("Аппаратное ускорение"));

test("tray acceleration toggle preserves config and relaunches after confirmation", async () => {
    for (const enabled of [true, false]) {
        const { state } = await boot({ hardwareAcceleration: !enabled, displayId: 20, custom: 42 });
        assert.equal(accelerationMenu(state).checked, !enabled);
        accelerationMenu(state).click({ checked: enabled });
        const config = JSON.parse(state.config);
        assert.equal(config.hardwareAcceleration, enabled);
        assert.equal(config.displayId, 20);
        assert.equal(config.custom, 42);
        assert.deepEqual(state.calls.slice(-2), ["relaunch", "quit"]);
    }
});

test("cancel and config failures do not restart or change acceleration", async () => {
    for (const failure of ["cancel", "read", "parse", "write"]) {
        const { state } = await boot();
        if (failure === "cancel") state.confirmResult = 1;
        if (failure === "read") state.readError = new Error("permission denied");
        if (failure === "parse") state.config = "broken JSON";
        if (failure === "write") state.configWriteError = true;
        const previous = state.config;
        accelerationMenu(state).click({ checked: false });
        assert.equal(state.config, previous);
        assert.equal(state.relaunch, undefined);
        assert.equal(state.calls.includes("quit"), false);
        assert.equal(accelerationMenu(state).checked, true);
        assert.equal(state.errors.length, failure === "cancel" ? 0 : 1);
    }
});

test("enabling acceleration removes explicit disable-gpu but preserves other launch arguments", async () => {
    const { state } = await boot({}, ["--disable-gpu", "--custom"]);
    assert.equal(accelerationMenu(state).checked, false);
    accelerationMenu(state).click({ checked: true });
    assert.deepEqual(Array.from(state.relaunch.args), ["app", "--custom"]);
});

test("stable ID wins; legacy primary-first index and invalid-index fallback remain", async () => {
    const { api } = await boot();
    assert.equal(api.getTargetDisplay({ displayId: "20", displayIndex: 0 }).id, 20);
    assert.equal(api.getTargetDisplay({ displayIndex: 0 }).id, 10);
    assert.equal(api.getTargetDisplay({ displayIndex: 1 }).id, 20);
    assert.equal(api.getTargetDisplay({ displayIndex: 99 }).id, 20);
    assert.equal(api.getTargetDisplay({ displayIndex: -1 }).id, 10);
    assert.equal(api.getTargetDisplay({ displayId: 999, displayIndex: 1 }).id, 20);
});

test("tray selection preserves current config, moves window and persists both selectors", async () => {
    const { state, api } = await boot({ zoomFactor: 1.5, overlayUrl: "http://custom/", custom: { keep: true } });
    const updated = JSON.parse(state.config);
    updated.externalEdit = 42;
    state.config = JSON.stringify(updated);
    api.seedBounds();
    monitorMenu(state).submenu[1].click();
    const saved = JSON.parse(state.config);
    assert.deepEqual(saved, { ...updated, displayId: 20, displayIndex: 1, hardwareAcceleration: true });
    assert.equal(state.window.bounds.x, -1920);
    assert.equal(monitorMenu(state).submenu[1].checked, true);
    assert.deepEqual(Array.from(api.bounds()), [null, null, null, 0, false, 0]);
    assert.equal(state.passthrough, true);
});

test("config read/parse/write failures do not overwrite settings or move selection", async () => {
    for (const failure of ["read", "parse", "write"]) {
        const { state, api } = await boot();
        if (failure === "read") state.readError = new Error("permission denied");
        if (failure === "parse") state.config = "broken JSON";
        if (failure === "write") state.configWriteError = true;
        const previous = state.config;
        api.selectDisplay(20);
        assert.equal(state.config, previous);
        assert.equal(api.getTargetDisplay().id, 10);
        assert.equal(state.errors.length, 1);
    }
});

test("hotplug restores saved ID, metrics reposition, submenu hides on one display", async () => {
    const { state, api, screen, app } = await boot({ displayId: 20, displayIndex: 1 });
    const original = state.config;
    state.displays = [display(10)];
    screen.emit("display-removed");
    assert.equal(state.window.bounds.x, 0);
    assert.equal(monitorMenu(state), undefined);
    state.displays.push(display(20, 1920));
    screen.emit("display-added");
    assert.equal(state.window.bounds.x, 1920);
    state.primary = 20;
    state.displays[1].bounds = { x: -2560, y: -100, width: 2560, height: 1440 };
    api.seedBounds();
    screen.emit("display-metrics-changed", {}, state.displays[1], ["bounds", "scaleFactor"]);
    assert.deepEqual(state.window.bounds, state.displays[1].bounds);
    assert.equal(state.config, original);
    assert.deepEqual(Array.from(api.bounds()), [null, null, null, 0, false, 0]);
    app.emit("before-quit");
    assert.equal(screen.listenerCount("display-added"), 0);
});

test("mixed-DPI round trips repair delayed native size changes without multiplying DIP bounds", async () => {
    const { state, api, runTimers } = await boot();
    state.displays[1].scaleFactor = 1.5;
    state.displays[1].bounds = { x: -1280, y: 0, width: 1280, height: 720 };
    for (const id of [20, 10, 20, 10]) {
        api.selectDisplay(id);
        assert.equal(state.resizable, true);
        state.window.bounds.width *= 1.5;
        state.window.bounds.height *= 1.5;
        runTimers();
        assert.deepEqual(state.window.bounds, state.displays.find((d) => d.id === id).bounds);
        assert.equal(state.resizable, false);
    }
});

test("rapid monitor switches cancel stale corrections and quitting cancels pending timers", async () => {
    const { state, api, runTimers, app } = await boot();
    api.selectDisplay(20);
    const stale = state.timers.values().next().value;
    api.selectDisplay(10);
    stale();
    runTimers();
    assert.deepEqual(state.window.bounds, state.displays[0].bounds);
    api.selectDisplay(20);
    app.emit("before-quit");
    assert.equal(state.timers.size, 0);
    assert.equal(state.lifetimeStopped, true);
});

test("native DPI notification defers geometry changes and destroyed windows cancel settlement", async () => {
    const { state, api, runTimers } = await boot();
    if (process.platform === "win32") {
        state.window.bounds = { x: 0, y: 0, width: 2880, height: 1620 };
        state.dpiMessage();
        assert.equal(state.boundsWrites, 0);
        runTimers();
        assert.deepEqual(state.window.bounds, state.displays[0].bounds);
    }
    api.selectDisplay(20);
    const writes = state.boundsWrites;
    state.window.destroyed = true;
    state.window.emit("closed");
    runTimers();
    assert.equal(state.boundsWrites, writes);
    assert.equal(state.timers.size, 0);
});

test("persistent native geometry failure has bounded retries", async () => {
    const { state, api, runTimers } = await boot();
    state.distortBounds = true;
    api.selectDisplay(20);
    runTimers();
    assert.equal(state.boundsWrites, 6);
    assert.equal(state.resizable, false);
});

test("game ownership survives GPU relaunch and lifetime watcher can quit the app", async () => {
    const { state } = await boot({}, ["--overlay-game-pid=1234"]);
    accelerationMenu(state).click({ checked: false });
    assert(Array.from(state.relaunch.args).includes("--overlay-game-pid=1234"));
    state.calls.length = 0;
    state.lifetimeWatcher.quit();
    assert.deepEqual(state.calls, ["quit"]);
});

test("screenshot filenames use local date/time with padding and no spaces", async () => {
    const { state, api } = await boot();
    assert.equal(api.screenshotFileName(new Date(2026, 0, 2, 3, 4, 5, 6)), "OverlayHUD_2026-01-02_03-04-05-006.png");
    await api.saveOverlayScreenshot();
    assert.match(state.dialogOptions.defaultPath, /^OverlayHUD_\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2}-\d{3}\.png$/);
});

test("stale async hover success/failure cannot restore or erase new bounds after movement", async () => {
    for (const rejectOld of [false, true]) {
        const { state, api, screen } = await boot();
        let resolve, reject;
        state.rectPromise = new Promise((yes, no) => { resolve = yes; reject = no; });
        const pending = api.refreshOverlayContentBounds();
        state.rectPromise = null;
        screen.emit("display-metrics-changed");
        await flush();
        api.seedBounds();
        if (rejectOld) reject(new Error("stale"));
        else resolve({ overlay: { left: 100, top: 100, right: 200, bottom: 200 } });
        await pending;
        assert.equal(api.bounds()[0].left, 1);
    }
});

test("screenshot captures renderer once, preserves PNG bytes, adds suffix and releases lock", async () => {
    const { state, api } = await boot();
    state.saveResult.filePath = "chosen-file";
    await api.saveOverlayScreenshot();
    assert.equal(state.captures, 1);
    assert.equal(state.dialogs, 1);
    assert.equal(state.writes[0].file, "chosen-file.png");
    assert.equal(state.writes[0].bytes.toString(), "mock PNG with alpha");
    assert.equal(api.busy(), false);
    assert.equal(state.errors.length, 0);
});

test("cancel and missing path write nothing; dialog phase rejects reentrancy", async () => {
    for (const result of [{ canceled: true }, { canceled: false }]) {
        const { state, api } = await boot();
        let release;
        state.savePromise = new Promise((resolve) => { release = resolve; });
        const pending = api.saveOverlayScreenshot();
        await flush();
        await api.saveOverlayScreenshot();
        assert.equal(state.captures, 1);
        assert.equal(api.busy(), true);
        release(result);
        await pending;
        assert.equal(state.writes.length, 0);
        assert.equal(state.errors.length, 0);
        assert.equal(api.busy(), false);
    }
});

test("capture phase rejects reentrancy and window destruction is reported", async () => {
    const { state, api, image } = await boot();
    let release;
    state.capturePromise = new Promise((resolve) => { release = resolve; });
    const pending = api.saveOverlayScreenshot();
    await api.saveOverlayScreenshot();
    assert.equal(state.captures, 1);
    state.window.destroyed = true;
    release(image());
    await pending;
    assert.equal(state.dialogs, 0);
    assert.equal(state.errors.length, 1);
    assert.equal(api.busy(), false);
});

test("capture/encoding/dialog/write failures and empty images are handled; retry works", async () => {
    for (const failure of ["captureError", "encodeError", "dialogError", "writeError", "empty", "emptyPNG"]) {
        const { state, api } = await boot();
        state[failure] = true;
        await api.saveOverlayScreenshot();
        assert.equal(state.errors.length, 1, failure);
        assert.equal(state.writes.length, 0, failure);
        assert.equal(api.busy(), false, failure);
        state[failure] = false;
        await api.saveOverlayScreenshot();
        assert.equal(state.writes.length, 1, failure);
    }
});

test("hidden/loading overlays are not captured", async () => {
    for (const loading of [false, true]) {
        const { state, api } = await boot();
        state.window.visible = loading;
        state.loading = loading;
        await api.saveOverlayScreenshot();
        assert.equal(state.captures, 0);
        assert.equal(state.errors.length, 1);
        assert.equal(api.busy(), false);
    }
});
