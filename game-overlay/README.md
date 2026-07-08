# OverlayHUD

Transparent click-through window that opens the existing web overlay above the game. The local overlay server is built in and starts automatically.

## Start

1. Use borderless or windowed mode in R.E.P.O. Exclusive fullscreen cannot be covered by a desktop window.
2. Run `start-game-overlay.bat` on the gaming PC.

The first launch installs Electron automatically. The default overlay address is local: `http://127.0.0.1:8787/overlay.html`. Edit `overlay-config.json` to select another monitor or override the address.

`displayIndex` is zero-based: `0` is the primary monitor; the remaining monitors are numbered from left to right.

## Portable build

Run `build-portable.bat`. The ready-to-copy application will appear in `dist\OverlayHUD-win32-x64`. It does not require Node.js on the gaming PC.

## Hotkeys

- `Ctrl+Alt+O`: show or hide the overlay.
- `Ctrl+Alt+I`: toggle click-through mode.
- `Ctrl+Alt+R`: reload the web overlay.
- `Ctrl+Alt+Q`: quit.

The same actions are available from the tray icon.
