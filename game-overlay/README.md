# OverlayHUD

Transparent click-through window that opens the existing web overlay above the game. The local overlay server is built in and starts automatically.

## Start

1. Use borderless or windowed mode in R.E.P.O. Exclusive fullscreen cannot be covered by a desktop window.
2. Run `start-game-overlay.bat` on the gaming PC.

The first launch installs Electron automatically. The default overlay address is local: `http://127.0.0.1:8787/overlay.html`. Edit `overlay-config.json` to select another monitor or override the address.

## Monitors and transparency

When multiple monitors are connected, the tray's **Монитор** submenu selects the overlay monitor and saves `displayId` and `displayIndex` in `overlay-config.json` (beside the executable in portable builds). Other config settings, including unknown keys, are preserved. Save failures are reported without changing the selection.

`displayId` takes priority and survives monitor ordering changes while the OS keeps the same ID. `displayIndex` remains the compatibility fallback: `0` is primary; other monitors are sorted left to right, then top to bottom. To select only by index manually, set `displayId` to `null`. If the selected display disappears, the index fallback is used without overwriting the saved ID, so reconnecting it restores the selection. Display addition/removal, resolution, layout and DPI changes reposition the window and invalidate cached hover hit regions. OS/driver changes may assign new display IDs. Mixed-DPI moves temporarily release fixed-size constraints and recheck native window bounds over a bounded settling interval, including Windows DPI-change notifications; screen bounds stay in DIP without applying a second DPI multiplier.

Hardware acceleration is **enabled by default**. If a transparent window renders black, uncheck **Аппаратное ускорение (перезапуск)…** in the tray and confirm the overlay restart; the game keeps running. The setting is saved in `overlay-config.json`, preserving other settings. Cancel or a save failure does not restart the app. You can also set `"hardwareAcceleration": false` manually and restart; only the JSON boolean `false` disables acceleration before Electron becomes ready. Software rendering may increase CPU usage and does not guarantee transparency on every OS/driver combination. The checkbox also reflects an explicit `--disable-gpu` launch flag; enabling acceleration via the tray removes that flag for the relaunch.

When the plugin launches the app with `OverlayApp.AutoClose=true`, it passes the game PID via `--overlay-game-pid`. The desktop app checks that process once per second and exits when it is gone, including after a GPU-setting relaunch. Manual launches and `AutoClose=false` do not enable this watcher. Updating this behavior requires both the plugin DLL and the desktop app.

## Overlay-only PNG screenshots

Use **Сохранить PNG оверлея (без игры)…** in the tray. The app captures the current Electron page with `webContents.capturePage()` and opens a PNG save dialog. It never uses desktop capture: the game, desktop, other windows and tray menu are not included. The suggested filename uses local capture date/time without spaces, for example `OverlayHUD_2026-10-12_21-04-05-006.png` (including milliseconds). The `.png` suffix is added if missing. Cancel does not write a file; capture, encoding, dialog and write errors are reported. Only one screenshot operation can run at a time.

The overlay must be visible and fully loaded. The capture is taken **before** the save dialog opens and includes the whole overlay window's page, including any visible HUD controls/tooltips. PNG retains the alpha supplied by Electron; opaque page elements are not removed. Window-level hover opacity is an OS compositor effect and is not necessarily represented in the captured page. A hidden/empty HUD can produce a transparent image, and DPI determines the actual pixel dimensions. Actual alpha/rendering fidelity depends on Electron and the graphics backend; a PNG viewer may display transparent pixels against black. This is not a screenshot of the game with HUD, and no desktop-capture fallback is used.

## Unit tests

Run `node --test game-overlay/main.test.js game-overlay/game-lifetime.test.js` from the repository root. Tests use mocked Electron/filesystem APIs and do not launch the app or capture a real screen. Validate transparency, mixed-DPI monitor movement and native save dialogs manually on the target Windows machine.

## Portable build

Run `build-portable.bat`. The ready-to-copy application will appear in `dist\OverlayHUD-win32-x64`. It does not require Node.js on the gaming PC.

Keep `--no-asar` in the packaging command. The source is shipped in `resources/app/`, not `resources/app.asar`: Electron-based mod managers can treat ASAR files as virtual directories and fail to remove them with `EBUSY`. The plugin recognizes both layouts. When upgrading an old ASAR installation, close the game, overlay and mod manager, back up `timestamps` and `overlay-config.json`, and remove the old extracted `OverlayHUD_app` directory in Explorer before installing the new bundle.

## Hotkeys

- `Ctrl+Alt+O`: show or hide the overlay.
- `Ctrl+Alt+I`: toggle click-through mode.
- `Ctrl+Alt+R`: reload the web overlay.
- `Ctrl+Alt+Q`: quit.

The same actions are available from the tray icon.
