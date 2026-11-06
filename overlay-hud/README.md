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

`POST /api/monster-localization` sends `{ "names": { "ENEMY.SNATCHER": { "en": "Pony", "ru": "Пони" } } }` from the game's `HUD` string tables. The plugin asynchronously loads `en-US` and `ru-RU` without changing the selected game locale, retries delivery on failure, and republishes every minute so a restarted desktop server receives the current names. Internal monster identities and counts are unchanged.

The fallback in `assets/monster-localization.json` is extracted from the installed game bundles (30 enemies in the current snapshot). Refresh it with `uv run --with UnityPy==1.25.4 python tools/extract-monster-localization.py "C:/Program Files (x86)/Steam/steamapps/common/REPO/REPO_Data"` from the repository root. The extraction joins shared string IDs with both locales and fails if either translation is missing. The normal Electron packaging command includes this file through `assets/`.

## Config

BepInEx creates `local.overlay.overlay_hud.cfg` after first launch. Current settings are:

- `Overlay.Endpoint` and `Overlay.LevelEndpoint` for the local HUD server.
- `Detection.ScanIntervalSeconds` for pending roster retries.
- `Detection.StatusIntervalSeconds` for periodic safety sync; event-driven health and respawn changes remain immediate, and values below 10 seconds are raised to 10.
- `Detection.PreferPlayerVisionDetection` for player-visible encounter detection, with legacy fallback for enemies that need it.
- `Debug.Logging` for extra diagnostic logs.
- `OverlayApp.AutoStart`, `OverlayApp.AutoClose`, `OverlayApp.ExecutableRelativePath`, and `OverlayApp.ArchiveName` for the bundled desktop app.

`OverlayApp.ExecutableRelativePath` defaults to empty, selecting the external per-profile install below. Known legacy default path strings are normalized to empty. Custom paths retain their previous behavior: relative to the plugin DLL or absolute, without this external migration. Older LAN endpoints are migrated to the current local defaults on launch.

## Desktop App Storage

`OverlayHUD_app.zip` stays beside the plugin DLL. The default executable is:

```text
%LOCALAPPDATA%\OverlayHUD\profiles\<profile-key>\app\OverlayHUD.exe
```

`<profile-key>` is the SHA-256 of the normalized `BepInExRootPath`. App files, root `overlay-config.json`, and `timestamps/` are isolated per BepInEx profile. Settings UI `localStorage` remains in Electron's existing storage; browser-profile isolation is unchanged.

Close **both the game and desktop app** before updating or preparing migration. Only desktop AutoStart invokes migration; Native HUD timestamps are unchanged. A recognized legacy `OverlayHUD_app` beside the DLL is copied in full, including config, timestamps, and unknown files. Archive updates preserve the root config and timestamps. The legacy folder is deleted only after successful migration; do not delete it beforehand. If both legacy and external installs exist, the legacy folder is left untouched with a warning for manual review. Locked cleanup also produces a warning.

An archive SHA-256 change triggers a staged upgrade without manual deletion. Failed updates preserve the original app and abort launch. Relocation only needs the updated plugin DLL with the existing bundle, not an Electron rebuild.

Look for `Bundled OverlayHUD desktop app ready at:` in `BepInEx/LogOutput.log` for the actual executable path. `Extracted bundled OverlayHUD desktop app` now applies only to custom installs.

External files survive mod uninstall or profile deletion. With the app closed, back up needed timestamps and root config, then manually delete the corresponding `%LOCALAPPDATA%\OverlayHUD\profiles\<profile-key>` folder if no longer needed.

## Build Notes

1. Install BepInEx for R.E.P.O. through r2modman/Thunderstore or manually.
2. Copy the needed DLL references into `../lib/` or change `OverlayHUD.csproj` `HintPath` values to point at your game install.
3. Build with `dotnet build overlay-hud/OverlayHUD.csproj -c Release`.
4. Copy `bin/Release/net472/OverlayHUD.dll` into the package folders before building a flat package.
5. Build the flat package from `package-flat` so it includes `OverlayHUD.dll`, `OverlayHUD_app.zip`, `manifest.json`, `README.md`, and `icon.png`.
