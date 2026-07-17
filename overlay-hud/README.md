# OverlayHUD

**Author: EgorSalad**

Small BepInEx plugin source for syncing R.E.P.O. runtime state with the bundled OverlayHUD desktop app.

The plugin does not reveal the whole level roster to the HUD as seen. It builds the roster for status tracking, then sends a monster as encountered only after the game reports a player-visible or legacy close/vision encounter. Tick and Upscream use the legacy fallback while PlayerVision detection is enabled because their `EnemyOnScreen` path is not reliable in current testing.

## Overlay Endpoints

The bundled desktop app starts a local server on `127.0.0.1:8787`. The plugin posts:

```http
POST http://127.0.0.1:8787/api/monster-seen
Content-Type: application/json

{"name":"Spewer","id":123}
```

It also posts level changes, overlay visibility, Tab hide state, player upgrades, map value, monster roster, and monster health/respawn status to sibling `/api/...` endpoints.

## Config

BepInEx creates `local.overlay.overlay_hud.cfg` after first launch. Current settings are:

- `Overlay.Endpoint` and `Overlay.LevelEndpoint` for the local HUD server.
- `Detection.ScanIntervalSeconds` for pending roster retries.
- `Detection.StatusIntervalSeconds` for periodic safety sync; event-driven health and respawn changes remain immediate, and values below 10 seconds are raised to 10.
- `Detection.PreferPlayerVisionDetection` for player-visible encounter detection, with legacy fallback for enemies that need it.
- `Debug.Logging` for extra diagnostic logs.
- `OverlayApp.AutoStart`, `OverlayApp.AutoClose`, `OverlayApp.ExecutableRelativePath`, and `OverlayApp.ArchiveName` for the bundled `OverlayHUD_app`.

Older LAN endpoints and earlier app folder names are migrated to the current local defaults on launch.

## Build Notes

1. Install BepInEx for R.E.P.O. through Gale/r2modman/Thunderstore or manually.
2. Copy the needed DLL references into `../lib/` or change `OverlayHUD.csproj` `HintPath` values to point at your game install.
3. Build with `dotnet build overlay-hud/OverlayHUD.csproj -c Release`.
4. Copy `bin/Release/net472/OverlayHUD.dll` into the package folders before building a flat package.
5. Build the flat package from `package-flat` so it includes `OverlayHUD.dll`, `OverlayHUD_app.zip`, `manifest.json`, `README.md`, and `icon.png`.
