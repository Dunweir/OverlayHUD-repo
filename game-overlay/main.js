const { app, BrowserWindow, Menu, Tray, dialog, globalShortcut, nativeImage, screen, shell } = require("electron");
const fs = require("fs");
const path = require("path");
const { startGameLifetimeWatcher } = require("./game-lifetime");

const portableRoot = app.isPackaged ? path.dirname(process.execPath) : __dirname;
const configPath = path.join(portableRoot, "overlay-config.json");
const defaultConfig = {
    overlayUrl: "http://127.0.0.1:8787/overlay.html",
    displayIndex: 0,
    displayId: null,
    hardwareAcceleration: true,
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
let overlayHotspotBounds = null;
let overlayDragBounds = [];
let overlayDragging = false;
let dragHoldUntil = 0;
let overlayControlsEnabled = false;
let hoverDimmed = false;
let hoverOpacity = 0.5;
let overlayOpacity = 1;
let opacityTarget = 1;
let opacityTimer = null;
let hoverTick = 0;
let quitting = false;
let localServerModule = null;
let hoverBoundsGeneration = 0;
let screenshotInProgress = false;
let stopGameLifetimeWatcher = () => {};
let displaySettleTimer = null;
let displayTransitionGeneration = 0;
let displayTransitionActive = false;
const startupConfig = readConfig();
let displayConfig = startupConfig;

// Transparent windows can render black with GPU compositing on some Windows drivers.
if (startupConfig.hardwareAcceleration === false) app.disableHardwareAcceleration();

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

function getOrderedDisplays() {
    const primary = screen.getPrimaryDisplay();
    const otherDisplays = screen.getAllDisplays().filter((display) => display.id !== primary.id).sort((left, right) => {
        return left.bounds.x - right.bounds.x || left.bounds.y - right.bounds.y;
    });
    return [primary, ...otherDisplays];
}

function getTargetDisplay(config = displayConfig) {
    const displays = getOrderedDisplays();
    const matched = config.displayId != null
        ? displays.find((display) => String(display.id) === String(config.displayId))
        : null;
    const index = Math.min(Math.max(Number(config.displayIndex) || 0, 0), displays.length - 1);
    return matched || displays[index] || screen.getPrimaryDisplay();
}

function invalidateHoverBounds() {
    ++hoverBoundsGeneration;
    overlayContentBounds = null;
    overlayInteractiveBounds = null;
    overlayHotspotBounds = null;
    overlayDragBounds = [];
    overlayDragging = false;
    dragHoldUntil = 0;
    overlayControlsEnabled = false;
    applyMousePassthrough(clickThrough);
    setHoverDimmed(false);
}

function cancelDisplayTransition() {
    clearTimeout(displaySettleTimer);
    displaySettleTimer = null;
    displayTransitionActive = false;
    ++displayTransitionGeneration;
}

function displayBoundsMatch(window, bounds) {
    const actual = window.getBounds();
    const content = window.getContentBounds();
    return ["x", "y", "width", "height"].every((key) => Math.abs(actual[key] - bounds[key]) <= 1)
        && Math.abs(content.width - bounds.width) <= 1
        && Math.abs(content.height - bounds.height) <= 1;
}

function repositionOverlay() {
    cancelDisplayTransition();
    const window = overlayWindow;
    if (window && !window.isDestroyed() && !quitting) {
        const generation = displayTransitionGeneration;
        displayTransitionActive = true;
        invalidateHoverBounds();
        // Fixed-size constraints can retain the old monitor's DPI during WM_DPICHANGED.
        // Keep bounds in DIP; Electron performs the physical-pixel conversion.
        window.setResizable(true);
        window.setBounds(getTargetDisplay().bounds, false);
        invalidateHoverBounds();
        refreshOverlayContentBounds();
        const delays = [50, 100, 200, 400];
        function settle(attempt) {
            if (generation !== displayTransitionGeneration || window !== overlayWindow || window.isDestroyed() || quitting) return;
            const bounds = getTargetDisplay().bounds;
            if (!displayBoundsMatch(window, bounds)) {
                window.setBounds(bounds, false);
                invalidateHoverBounds();
            }
            if (attempt < delays.length - 1) {
                displaySettleTimer = setTimeout(() => settle(attempt + 1), delays[attempt + 1]);
                return;
            }
            window.setResizable(false);
            // Verify once more after restoring fixed-size constraints, without a resize loop.
            displaySettleTimer = setTimeout(() => {
                if (generation !== displayTransitionGeneration || window !== overlayWindow || window.isDestroyed() || quitting) return;
                const finalBounds = getTargetDisplay().bounds;
                if (!displayBoundsMatch(window, finalBounds)) window.setBounds(finalBounds, false);
                if (!displayBoundsMatch(window, finalBounds)) {
                    console.warn("Overlay bounds did not settle after DPI change", { expected: finalBounds, actual: window.getBounds() });
                }
                displaySettleTimer = null;
                displayTransitionActive = false;
                invalidateHoverBounds();
                refreshOverlayContentBounds();
            }, 100);
        }
        displaySettleTimer = setTimeout(() => settle(0), delays[0]);
    }
    rebuildTrayMenu();
}

function selectDisplay(displayId) {
    try {
        const displays = getOrderedDisplays();
        const displayIndex = displays.findIndex((display) => display.id === displayId);
        if (displayIndex < 0) return repositionOverlay();
        displayConfig = saveConfig({ displayId, displayIndex });
        repositionOverlay();
    } catch (error) {
        dialog.showErrorBox("OverlayHUD", `Не удалось сохранить монитор:\n${error.message}`);
        rebuildTrayMenu();
    }
}

function saveConfig(changes) {
    // Never overwrite an unreadable config or discard unrelated settings.
    let existing = {};
    try {
        existing = JSON.parse(fs.readFileSync(configPath, "utf8"));
    } catch (error) {
        if (error.code !== "ENOENT") throw error;
    }
    if (!existing || typeof existing !== "object" || Array.isArray(existing)) {
        throw new Error("Invalid overlay config object");
    }
    const config = { ...defaultConfig, ...existing, ...changes };
    fs.writeFileSync(configPath, `${JSON.stringify(config, null, 2)}\n`, "utf8");
    return config;
}

function setHardwareAcceleration(enabled) {
    try {
        const choice = dialog.showMessageBoxSync({
            type: "question",
            title: "OverlayHUD",
            message: `${enabled ? "Включить" : "Отключить"} аппаратное ускорение?`,
            detail: "Для применения настройки приложение оверлея будет перезапущено. Игра продолжит работать.",
            buttons: ["Перезапустить оверлей", "Отмена"],
            defaultId: 0,
            cancelId: 1
        });
        if (choice !== 0) return;
        saveConfig({ hardwareAcceleration: enabled });
        // An explicit disable-gpu argument would otherwise survive re-enabling via the tray.
        const args = process.argv.slice(1).filter((arg) => !enabled || !/^--disable-gpu(?:=.*)?$/.test(arg));
        app.relaunch({ args });
        app.quit();
    } catch (error) {
        dialog.showErrorBox("OverlayHUD", `Не удалось изменить аппаратное ускорение:\n${error.message}`);
    } finally {
        rebuildTrayMenu();
    }
}

function screenshotFileName(now = new Date()) {
    const pad = (value, length = 2) => String(value).padStart(length, "0");
    return `OverlayHUD_${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}_${pad(now.getHours())}-${pad(now.getMinutes())}-${pad(now.getSeconds())}-${pad(now.getMilliseconds(), 3)}.png`;
}

async function saveOverlayScreenshot() {
    if (screenshotInProgress) return;
    screenshotInProgress = true;
    rebuildTrayMenu();
    try {
        const window = overlayWindow;
        if (!window || window.isDestroyed() || !window.isVisible() || window.webContents.isLoading()) {
            throw new Error("Покажите оверлей и дождитесь загрузки страницы.");
        }
        // Capture only this renderer surface, never the desktop or the game beneath it.
        const fileName = screenshotFileName();
        const image = await window.webContents.capturePage();
        if (window.isDestroyed() || quitting) throw new Error("Окно оверлея закрыто.");
        if (image.isEmpty()) throw new Error("Electron вернул пустой снимок.");
        const png = image.toPNG();
        if (!png.length) throw new Error("Не удалось сформировать PNG.");
        const result = await dialog.showSaveDialog({
            title: "Сохранить PNG оверлея (без игры)",
            defaultPath: fileName,
            filters: [{ name: "PNG image", extensions: ["png"] }],
            properties: ["showOverwriteConfirmation"]
        });
        if (result.canceled || !result.filePath || quitting) return;
        const filePath = /\.png$/i.test(result.filePath) ? result.filePath : `${result.filePath}.png`;
        await fs.promises.writeFile(filePath, png);
    } catch (error) {
        console.error(`Overlay screenshot failed: ${error.message}`);
        if (!quitting) dialog.showErrorBox("OverlayHUD", `Не удалось сохранить снимок оверлея:\n${error.message}`);
    } finally {
        screenshotInProgress = false;
        rebuildTrayMenu();
    }
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

function getOverlayHotspotFallbackBounds() {
    if (!overlayWindow || overlayWindow.isDestroyed()) return null;
    const bounds = overlayWindow.getBounds();
    const edgeOffset = 20;
    return {
        left: bounds.x + bounds.width - edgeOffset - 80,
        top: bounds.y + bounds.height - edgeOffset - 54,
        right: bounds.x + bounds.width - edgeOffset,
        bottom: bounds.y + bounds.height - edgeOffset
    };
}

async function setOverlayHoverState(isOpen, cursor) {
    if (!overlayWindow || overlayWindow.isDestroyed() || overlayWindow.webContents.isLoading()) return;
    try {
        const windowBounds = overlayWindow.getBounds();
        const zoom = overlayWindow.webContents.getZoomFactor();
        const clientX = (cursor.x - windowBounds.x) / zoom;
        const clientY = (cursor.y - windowBounds.y) / zoom;
        await overlayWindow.webContents.executeJavaScript(`(() => {
            const controls = document.getElementById("overlayHoverControls");
            if (controls) controls.classList.toggle("is-open", ${Boolean(isOpen)});

            const clientX = ${clientX.toFixed(2)};
            const clientY = ${clientY.toFixed(2)};
            document.querySelectorAll(".is-tooltip-active").forEach((element) => {
                const bounds = element.getBoundingClientRect();
                const active = clientX >= bounds.left && clientX <= bounds.right
                    && clientY >= bounds.top && clientY <= bounds.bottom;
                if (!active) element.classList.remove("is-tooltip-active");
            });
            const target = document.elementFromPoint(clientX, clientY);
            const tooltipTarget = target ? target.closest(".upgrade-stat, .monster-square") : null;
            if (tooltipTarget) tooltipTarget.classList.add("is-tooltip-active");
        })()`, true);
    } catch {
        // The page can be between reloads during startup; the next hover tick will retry.
    }
}

async function refreshOverlayContentBounds() {
    if (!overlayWindow || overlayWindow.isDestroyed() || overlayWindow.webContents.isLoading()) return;
    const window = overlayWindow;
    const generation = hoverBoundsGeneration;
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
            const overlayDragCorner = document.querySelector(".overlay-drag-corner-main");
            const controlsDragCorner = document.querySelector(".overlay-drag-corner-controls");
            const controlsOpen = controls && controls.matches(":hover");
            const hoverOpacity = Number.isFinite(Number(controls?.dataset.hoverOpacity)) ? Number(controls.dataset.hoverOpacity) / 100 : 0.5;
            const overlayVisible = overlay && overlay.classList.contains("is-visible") && !overlay.classList.contains("is-tab-hidden");
            const controlsEnabled = Boolean(controls && controls.classList.contains("is-enabled"));
            return {
                overlay: overlayVisible
                    ? toRect(overlay)
                    : null,
                controlsEnabled,
                controls: controlsEnabled
                    ? unionRects([toRect(hotspot), toRect(controlsDragCorner), controlsOpen || controls.classList.contains("is-open") ? toRect(panel) : null])
                    : null,
                hotspot: controlsEnabled
                    ? unionRects([toRect(hotspot), toRect(controlsDragCorner)])
                    : null,
                drag: [
                    overlayVisible ? toRect(overlayDragCorner) : null,
                    controlsEnabled ? toRect(controlsDragCorner) : null
                ].filter(Boolean),
                dragging: Boolean(document.documentElement.dataset.overlayDragging),
                hoverOpacity
            };
        })()`);
        if (window !== overlayWindow || window.isDestroyed() || generation !== hoverBoundsGeneration) return;
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
        overlayDragging = Boolean(rects?.dragging);
        overlayControlsEnabled = Boolean(rects?.controlsEnabled);
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
        if (rects && rects.hotspot) {
            const windowBounds = overlayWindow.getBounds();
            const zoom = overlayWindow.webContents.getZoomFactor();
            overlayHotspotBounds = {
                left: windowBounds.x + rects.hotspot.left * zoom,
                top: windowBounds.y + rects.hotspot.top * zoom,
                right: windowBounds.x + rects.hotspot.right * zoom,
                bottom: windowBounds.y + rects.hotspot.bottom * zoom
            };
        } else {
            overlayHotspotBounds = null;
        }
        if (rects && Array.isArray(rects.drag)) {
            const windowBounds = overlayWindow.getBounds();
            const zoom = overlayWindow.webContents.getZoomFactor();
            overlayDragBounds = rects.drag.map((rect) => ({
                left: windowBounds.x + rect.left * zoom,
                top: windowBounds.y + rect.top * zoom,
                right: windowBounds.x + rect.right * zoom,
                bottom: windowBounds.y + rect.bottom * zoom
            }));
        } else {
            overlayDragBounds = [];
        }
    } catch {
        if (window !== overlayWindow || window.isDestroyed() || generation !== hoverBoundsGeneration) return;
        overlayContentBounds = null;
        overlayInteractiveBounds = null;
        overlayHotspotBounds = null;
        overlayDragBounds = [];
        overlayDragging = false;
        overlayControlsEnabled = false;
        setHoverDimmed(false);
    }
}

function startHoverTracking() {
    clearInterval(hoverTimer);
    hoverTick = 0;
    refreshOverlayContentBounds();
    hoverTimer = setInterval(() => {
        if (++hoverTick % 5 === 0) refreshOverlayContentBounds();
        const cursor = screen.getCursorScreenPoint();
        const isOverControls = overlayInteractiveBounds
            && cursor.x >= overlayInteractiveBounds.left
            && cursor.x <= overlayInteractiveBounds.right
            && cursor.y >= overlayInteractiveBounds.top
            && cursor.y <= overlayInteractiveBounds.bottom;
        const hotspotBounds = overlayHotspotBounds || (overlayControlsEnabled ? getOverlayHotspotFallbackBounds() : null);
        const isOverHotspot = hotspotBounds
            && cursor.x >= hotspotBounds.left
            && cursor.x <= hotspotBounds.right
            && cursor.y >= hotspotBounds.top
            && cursor.y <= hotspotBounds.bottom;
        const isOverDrag = overlayDragBounds.some((bounds) => cursor.x >= bounds.left
            && cursor.x <= bounds.right
            && cursor.y >= bounds.top
            && cursor.y <= bounds.bottom);
        if (isOverDrag || overlayDragging) {
            dragHoldUntil = Date.now() + 500;
        }
        const isDragHeld = overlayDragging || Date.now() < dragHoldUntil;
        const isOverOverlay = overlayContentBounds
            && cursor.x >= overlayContentBounds.left
            && cursor.x <= overlayContentBounds.right
            && cursor.y >= overlayContentBounds.top
            && cursor.y <= overlayContentBounds.bottom;
        applyMousePassthrough(clickThrough && !isOverControls && !isOverHotspot && !isDragHeld);
        setOverlayHoverState(isOverControls || isOverHotspot, cursor);

        if (!overlayContentBounds) {
            setHoverDimmed(false);
            return;
        }
        setHoverDimmed(isOverOverlay);
    }, 100);
}

function rebuildTrayMenu() {
    if (!tray) return;
    const visible = overlayWindow && !overlayWindow.isDestroyed() && overlayWindow.isVisible();
    const displays = getOrderedDisplays();
    const target = getTargetDisplay();
    tray.setContextMenu(Menu.buildFromTemplate([

        { label: "Обновить", click: reloadOverlay },
        ...(displays.length > 1 ? [{
            label: "Монитор",
            submenu: displays.map((display, index) => ({
                label: `${index + 1}: ${display.label || "Монитор"} (${display.bounds.width}×${display.bounds.height})${index === 0 ? " — основной" : ""}`,
                type: "radio",
                checked: display.id === target.id,
                click: () => selectDisplay(display.id)
            }))
        }] : []),
        {
            label: "Сделать скриншот",
            enabled: Boolean(visible) && !screenshotInProgress,
            click: saveOverlayScreenshot
        },
        {
            label: "Прочее",
            submenu: [
                { label: visible ? "Скрыть оверлей" : "Показать оверлей", click: toggleOverlay },
                {
                    label: "Взаимодействовать с оверлеем",
                    type: "checkbox",
                    checked: !clickThrough,
                    click: (item) => applyClickThrough(!item.checked)
                }
            ]
        },
        {
            label: "Аппаратное ускорение (перезапуск)…",
            type: "checkbox",
            checked: startupConfig.hardwareAcceleration !== false && !app.commandLine.hasSwitch("disable-gpu"),
            click: (item) => setHardwareAcceleration(item.checked)
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
    displayConfig = config;
    const display = getTargetDisplay();
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

    overlayWindow.webContents.setWindowOpenHandler(({ url }) => {
        if (url.startsWith("http://") || url.startsWith("https://")) shell.openExternal(url);
        return { action: "deny" };
    });
    overlayWindow.webContents.on("will-navigate", (event, url) => {
        if (url === overlayWindow.webContents.getURL()) return;
        event.preventDefault();
        if (url.startsWith("http://") || url.startsWith("https://")) shell.openExternal(url);
    });

    if (process.platform === "win32") {
        overlayWindow.hookWindowMessage(0x02E0, () => {
            // Do not change bounds recursively inside the native DPI message handler.
            if (displayTransitionActive || quitting) return;
            clearTimeout(displaySettleTimer);
            displaySettleTimer = setTimeout(repositionOverlay, 0);
        });
    }
    overlayWindow.on("move", invalidateHoverBounds);
    overlayWindow.on("resize", invalidateHoverBounds);
    overlayWindow.webContents.on("did-start-loading", invalidateHoverBounds);
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
        cancelDisplayTransition();
        clearInterval(hoverTimer);
        clearInterval(opacityTimer);
        hoverTimer = null;
        opacityTimer = null;
        overlayContentBounds = null;
        overlayInteractiveBounds = null;
        overlayHotspotBounds = null;
        overlayDragBounds = [];
        overlayDragging = false;
        dragHoldUntil = 0;
        overlayControlsEnabled = false;
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
    const iconPath = app.isPackaged
        ? path.join(process.resourcesPath, "assets", "icons", "icon.png")
        : path.join(__dirname, "..", "assets", "icons", "icon.png");
    const icon = nativeImage.createFromPath(iconPath).resize({ width: 16, height: 16 });
    tray = new Tray(icon);
    tray.setToolTip("OverlayHUD");
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
            dialog.showErrorBox("OverlayHUD", `Не удалось запустить локальный сервер:\n${error.message}`);
            app.quit();
            return;
        }
        createOverlayWindow();
        createTray();
        registerShortcuts();
        screen.on("display-added", repositionOverlay);
        screen.on("display-removed", repositionOverlay);
        screen.on("display-metrics-changed", repositionOverlay);
        stopGameLifetimeWatcher = startGameLifetimeWatcher({ quit: () => app.quit() });
    });
}

app.on("before-quit", () => {
    quitting = true;
    stopGameLifetimeWatcher();
    cancelDisplayTransition();
    screen.removeListener("display-added", repositionOverlay);
    screen.removeListener("display-removed", repositionOverlay);
    screen.removeListener("display-metrics-changed", repositionOverlay);
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
