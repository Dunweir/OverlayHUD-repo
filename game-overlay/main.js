const { app, BrowserWindow, Menu, Tray, dialog, globalShortcut, nativeImage, screen } = require("electron");
const fs = require("fs");
const path = require("path");

const portableRoot = app.isPackaged ? path.dirname(process.execPath) : __dirname;
const configPath = path.join(portableRoot, "overlay-config.json");
const defaultConfig = {
    overlayUrl: "http://127.0.0.1:8787/overlay.html",
    displayIndex: 0,
    zoomFactor: 1
};

let overlayWindow = null;
let tray = null;
let clickThrough = true;
let mousePassthrough = true;
let retryTimer = null;
let hoverTimer = null;
let overlayContentBounds = null;
let overlayInteractiveBounds = null;
let hoverDimmed = false;
let hoverOpacity = 0.5;
let overlayOpacity = 1;
let opacityTarget = 1;
let opacityTimer = null;
let hoverTick = 0;
let quitting = false;
let localServerModule = null;

function readConfig() {
    try {
        const config = { ...defaultConfig, ...JSON.parse(fs.readFileSync(configPath, "utf8")) };
        if (config.overlayUrl === "http://192.168.1.198:8787/overlay.html") {
            config.overlayUrl = defaultConfig.overlayUrl;
            fs.writeFileSync(configPath, `${JSON.stringify(config, null, 2)}\n`, "utf8");
        }
        return config;
    } catch (error) {
        console.error(`Could not read ${configPath}: ${error.message}`);
        return { ...defaultConfig };
    }
}

async function startLocalServer() {
    const serverModulePath = app.isPackaged
        ? path.join(process.resourcesPath, "server.js")
        : path.join(__dirname, "..", "server.js");
    const assetRoot = app.isPackaged ? process.resourcesPath : path.join(__dirname, "..");
    localServerModule = require(serverModulePath);

    try {
        await localServerModule.startOverlayServer({ host: "127.0.0.1", port: 8787, root: assetRoot });
    } catch (error) {
        if (error.code === "EADDRINUSE") {
            console.warn("Port 8787 is already in use; using the existing local overlay server.");
            localServerModule = null;
            return;
        }
        throw error;
    }
}

function getTargetDisplay(displayIndex) {
    const primary = screen.getPrimaryDisplay();
    const otherDisplays = screen.getAllDisplays().filter((display) => display.id !== primary.id).sort((left, right) => {
        return left.bounds.x - right.bounds.x || left.bounds.y - right.bounds.y;
    });
    const displays = [primary, ...otherDisplays];
    const index = Math.min(Math.max(Number(displayIndex) || 0, 0), displays.length - 1);
    return displays[index] || screen.getPrimaryDisplay();
}

function applyClickThrough(enabled) {
    if (!overlayWindow || overlayWindow.isDestroyed()) return;
    clickThrough = enabled;
    applyMousePassthrough(enabled);
    rebuildTrayMenu();
}

function applyMousePassthrough(enabled) {
    if (!overlayWindow || overlayWindow.isDestroyed() || mousePassthrough === enabled) return;
    mousePassthrough = enabled;
    overlayWindow.setIgnoreMouseEvents(enabled, { forward: true });
    overlayWindow.setFocusable(!enabled);
}

function toggleOverlay() {
    if (!overlayWindow || overlayWindow.isDestroyed()) return;
    if (overlayWindow.isVisible()) overlayWindow.hide();
    else overlayWindow.showInactive();
    rebuildTrayMenu();
}

function reloadOverlay() {
    if (!overlayWindow || overlayWindow.isDestroyed()) return;
    clearTimeout(retryTimer);
    overlayWindow.loadURL(readConfig().overlayUrl);
}

function animateOverlayOpacity(targetOpacity) {
    if (!overlayWindow || overlayWindow.isDestroyed()) return;

    const target = Math.min(1, Math.max(0.2, targetOpacity));
    if (Math.abs(opacityTarget - target) < 0.001 && !opacityTimer) return;

    opacityTarget = target;
    clearInterval(opacityTimer);

    const start = overlayOpacity;
    const startedAt = Date.now();
    const duration = 180;

    opacityTimer = setInterval(() => {
        if (!overlayWindow || overlayWindow.isDestroyed()) {
            clearInterval(opacityTimer);
            opacityTimer = null;
            return;
        }

        const progress = Math.min(1, (Date.now() - startedAt) / duration);
        const eased = 1 - Math.pow(1 - progress, 3);
        overlayOpacity = start + (target - start) * eased;
        overlayWindow.setOpacity(overlayOpacity);

        if (progress >= 1) {
            overlayOpacity = target;
            overlayWindow.setOpacity(target);
            clearInterval(opacityTimer);
            opacityTimer = null;
        }
    }, 16);
}

function setHoverDimmed(dimmed) {
    if (!overlayWindow || overlayWindow.isDestroyed()) return;
    const targetOpacity = dimmed ? hoverOpacity : 1;
    if (hoverDimmed === dimmed && Math.abs(opacityTarget - targetOpacity) < 0.001) return;
    hoverDimmed = dimmed;
    animateOverlayOpacity(targetOpacity);
}

async function refreshOverlayContentBounds() {
    if (!overlayWindow || overlayWindow.isDestroyed() || overlayWindow.webContents.isLoading()) return;
    try {
        const rects = await overlayWindow.webContents.executeJavaScript(`(() => {
            const toRect = (element) => {
                if (!element) return null;
                const bounds = element.getBoundingClientRect();
                return { left: bounds.left, top: bounds.top, right: bounds.right, bottom: bounds.bottom };
            };
            const unionRects = (rects) => {
                const present = rects.filter(Boolean);
                if (present.length === 0) return null;
                return {
                    left: Math.min(...present.map((rect) => rect.left)),
                    top: Math.min(...present.map((rect) => rect.top)),
                    right: Math.max(...present.map((rect) => rect.right)),
                    bottom: Math.max(...present.map((rect) => rect.bottom))
                };
            };
            const overlay = document.getElementById("overlayContainer");
            const controls = document.getElementById("overlayHoverControls");
            const panel = controls ? controls.querySelector(".overlay-control-panel") : null;
            const hotspot = document.getElementById("overlayControlHotspot");
            const controlsOpen = controls && controls.matches(":hover");
            const hoverOpacity = Number.isFinite(Number(controls?.dataset.hoverOpacity)) ? Number(controls.dataset.hoverOpacity) / 100 : 0.5;
            return {
                overlay: overlay && overlay.classList.contains("is-visible") && !overlay.classList.contains("is-tab-hidden")
                    ? toRect(overlay)
                    : null,
                controls: controls && controls.classList.contains("is-enabled")
                    ? unionRects([toRect(hotspot), controlsOpen ? toRect(panel) : null])
                    : null,
                hoverOpacity
            };
        })()`);
        if (!rects || !rects.overlay) {
            overlayContentBounds = null;
            setHoverDimmed(false);
        } else {
            const windowBounds = overlayWindow.getBounds();
            const zoom = overlayWindow.webContents.getZoomFactor();
            overlayContentBounds = {
                left: windowBounds.x + rects.overlay.left * zoom,
                top: windowBounds.y + rects.overlay.top * zoom,
                right: windowBounds.x + rects.overlay.right * zoom,
                bottom: windowBounds.y + rects.overlay.bottom * zoom
            };
        }

        hoverOpacity = Math.min(1, Math.max(0.2, Number(rects?.hoverOpacity) || 0.5));
        if (rects && rects.controls) {
            const windowBounds = overlayWindow.getBounds();
            const zoom = overlayWindow.webContents.getZoomFactor();
            overlayInteractiveBounds = {
                left: windowBounds.x + rects.controls.left * zoom,
                top: windowBounds.y + rects.controls.top * zoom,
                right: windowBounds.x + rects.controls.right * zoom,
                bottom: windowBounds.y + rects.controls.bottom * zoom
            };
        } else {
            overlayInteractiveBounds = null;
        }
    } catch {
        overlayContentBounds = null;
        overlayInteractiveBounds = null;
        setHoverDimmed(false);
    }
}

function startHoverTracking() {
    clearInterval(hoverTimer);
    hoverTick = 0;
    refreshOverlayContentBounds();
    hoverTimer = setInterval(() => {
        if (++hoverTick % 2 === 0) refreshOverlayContentBounds();
        const cursor = screen.getCursorScreenPoint();
        const isOverControls = overlayInteractiveBounds
            && cursor.x >= overlayInteractiveBounds.left
            && cursor.x <= overlayInteractiveBounds.right
            && cursor.y >= overlayInteractiveBounds.top
            && cursor.y <= overlayInteractiveBounds.bottom;
        applyMousePassthrough(clickThrough && !isOverControls);

        if (!overlayContentBounds) {
            setHoverDimmed(false);
            return;
        }
        setHoverDimmed(cursor.x >= overlayContentBounds.left
            && cursor.x <= overlayContentBounds.right
            && cursor.y >= overlayContentBounds.top
            && cursor.y <= overlayContentBounds.bottom);
    }, 100);
}

function rebuildTrayMenu() {
    if (!tray) return;
    const visible = overlayWindow && !overlayWindow.isDestroyed() && overlayWindow.isVisible();
    tray.setContextMenu(Menu.buildFromTemplate([
        { label: visible ? "Скрыть оверлей" : "Показать оверлей", click: toggleOverlay },
        { label: "Обновить", click: reloadOverlay },
        {
            label: "Пропускать клики",
            type: "checkbox",
            checked: clickThrough,
            click: (item) => applyClickThrough(item.checked)
        },
        { type: "separator" },
        { label: "Выход", click: () => app.quit() }
    ]));
}

function scheduleRetry() {
    clearTimeout(retryTimer);
    retryTimer = setTimeout(() => {
        if (overlayWindow && !overlayWindow.isDestroyed()) reloadOverlay();
    }, 5000);
}

function createOverlayWindow() {
    const config = readConfig();
    const display = getTargetDisplay(config.displayIndex);
    const { x, y, width, height } = display.bounds;

    overlayWindow = new BrowserWindow({
        x,
        y,
        width,
        height,
        transparent: true,
        backgroundColor: "#00000000",
        frame: false,
        show: false,
        resizable: false,
        movable: false,
        minimizable: false,
        maximizable: false,
        fullscreenable: false,
        alwaysOnTop: true,
        focusable: false,
        skipTaskbar: true,
        hasShadow: false,
        webPreferences: {
            backgroundThrottling: false,
            contextIsolation: true,
            nodeIntegration: false,
            sandbox: true
        }
    });

    overlayWindow.setAlwaysOnTop(true, "screen-saver", 1);
    overlayWindow.setIgnoreMouseEvents(true, { forward: true });
    mousePassthrough = true;
    overlayWindow.webContents.setZoomFactor(Math.max(0.25, Number(config.zoomFactor) || 1));

    overlayWindow.webContents.on("did-finish-load", () => {
        clearTimeout(retryTimer);
        overlayWindow.showInactive();
        startHoverTracking();
        rebuildTrayMenu();
    });
    overlayWindow.webContents.on("did-fail-load", (_event, errorCode, errorDescription) => {
        if (errorCode === -3) return;
        console.error(`Overlay load failed: ${errorCode} ${errorDescription}`);
        scheduleRetry();
    });
    overlayWindow.on("closed", () => {
        clearInterval(hoverTimer);
        clearInterval(opacityTimer);
        hoverTimer = null;
        opacityTimer = null;
        overlayContentBounds = null;
        overlayInteractiveBounds = null;
        hoverDimmed = false;
        overlayOpacity = 1;
        opacityTarget = 1;
        overlayWindow = null;
        if (!quitting) app.quit();
    });

    overlayWindow.loadURL(config.overlayUrl);
}

function registerShortcuts() {
    globalShortcut.register("CommandOrControl+Alt+O", toggleOverlay);
    globalShortcut.register("CommandOrControl+Alt+I", () => applyClickThrough(!clickThrough));
    globalShortcut.register("CommandOrControl+Alt+R", reloadOverlay);
    globalShortcut.register("CommandOrControl+Alt+Q", () => app.quit());
}

function createTray() {
    const externalIconPath = path.join(portableRoot, "icon.png");
    const iconPath = fs.existsSync(externalIconPath) ? externalIconPath : path.join(__dirname, "icon.png");
    const icon = nativeImage.createFromPath(iconPath).resize({ width: 16, height: 16 });
    tray = new Tray(icon);
    tray.setToolTip("R.E.P.O. Game Overlay");
    tray.on("double-click", toggleOverlay);
    rebuildTrayMenu();
}

if (!app.requestSingleInstanceLock()) {
    app.quit();
} else {
    app.on("second-instance", () => {
        if (overlayWindow && !overlayWindow.isDestroyed()) overlayWindow.showInactive();
    });

    app.whenReady().then(async () => {
        try {
            await startLocalServer();
        } catch (error) {
            dialog.showErrorBox("R.E.P.O. Game Overlay", `Не удалось запустить локальный сервер:\n${error.message}`);
            app.quit();
            return;
        }
        createOverlayWindow();
        createTray();
        registerShortcuts();
    });
}

app.on("before-quit", () => {
    quitting = true;
    clearTimeout(retryTimer);
    clearInterval(hoverTimer);
    if (localServerModule) localServerModule.stopOverlayServer();
});

app.on("will-quit", () => {
    globalShortcut.unregisterAll();
});

app.on("window-all-closed", () => {
    app.quit();
});
