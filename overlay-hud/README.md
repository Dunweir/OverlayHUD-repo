# OverlayHUD

**Author: EgorSalad**

BepInEx plugin for displaying R.E.P.O. runtime state in a Unity HUD or a bundled desktop app.

Two alternative distributions share the same plugin GUID, manifest identity and version 26.11.4:

- **Full**: Native + Electron; `Renderer = Electron` by default.
- **Native-only**: a separately compiled Unity HUD DLL, without desktop startup/storage or HTTP bridge code, an app archive, a renderer selector, or desktop config entries.

Install only one distribution per profile. Remove the previous package before switching; do not leave duplicate DLLs or package remnants. Close the game, back up and remove `BepInEx/config/local.overlay.overlay_hud.cfg` when switching builds or upgrading from the old config schema. Old config sections/values are not migrated. Reapply desired settings using the new sections.

The plugin does not reveal the whole level roster to the HUD as seen. It builds the roster for status tracking, then sends a monster as encountered only after the game reports a player-visible or legacy close/vision encounter. Tick and Upscream use the legacy fallback while PlayerVision detection is enabled because their `EnemyOnScreen` path is not reliable in current testing.

## Overlay Endpoints (Full, Electron mode only)

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

BepInEx creates `BepInEx/config/local.overlay.overlay_hud.cfg` after first launch.

| Section | Settings |
| --- | --- |
| `01 General` (Full only) | `Renderer = Electron`; alternative `Native`, restart required |
| `02 Detection` | `ScanIntervalSeconds = 3`, `StatusIntervalSeconds = 10` (minimum 10), `RequireLineOfSight = true`, `RevealUnknownMonsters = false`, `PreferPlayerVisionDetection = true` |
| `03 Native - General` | `Enabled = true`, `Language = ru`, `SaveTimestamps = true` |
| `04 Native - Layout` | `Alignment = Center`, `Scale = 1`, `OffsetX = 0`, `OffsetY = 20`, `HorizontalLayout = true`, `HorizontalColumns = 11`, `Columns = 4`, `MonsterSize = 70`, `UpgradeSize = 38`, `UpgradeRows = 2`, `WrapValueLabels = true` |
| `05 Native - Information` | `ShowLevel`, `ShowTimer`, `ShowMapValue`, `ShowLostValue`, `ShowUpgrades` (all true) |
| `06 Native - Monsters` | `ShowMonsters`, `ShowHealth`, `ShowRespawnTimer`, `ShowRespawnIndicator`, `ShowStrength`, `ShowMonsterCounts` (true); `OnlyAliveMonsters`, `FilterUndetectedMonsters` (false) |
| `07 Native - Effects` | `Animations`, `ShowProximityWaves`, `ShowDamageNumbers`, `UseGameUIEffects` (true); `ShowDestroyedItemAlerts`, `ShowTooltips` (false) |
| `08 Native - Upgrades` | Per-upgrade visibility, all true |
| `09 Electron - App` (Full only) | `AutoStart = true`, `AutoClose = true`, `ExecutableRelativePath` empty, `ArchiveName = OverlayHUD_app.zip` |
| `10 Electron - Connection` (Full only) | `Endpoint = http://127.0.0.1:8787/api/monster-seen`, `LevelEndpoint = http://127.0.0.1:8787/api/level` |
| `99 Diagnostics` | `Logging = false` |

Sections `03`–`08` apply live through Configuration Manager or a BepInEx config reload; the cfg file is not watched automatically. Native-only does not bind sections `01`, `09`, or `10`. It does not fall back to another renderer if HUD initialization fails.

`ExecutableRelativePath` in `09 Electron - App` defaults to empty, selecting the external per-profile install below. Custom paths are relative to the plugin DLL or absolute, without external relocation. App-file relocation is independent of config migration: old sections and values have no compatibility layer.

## Desktop App Storage (Full only)

`OverlayHUD_app.zip` stays beside the plugin DLL. The default executable is:

```text
%LOCALAPPDATA%\OverlayHUD\profiles\<profile-key>\app\OverlayHUD.exe
```

`<profile-key>` is the SHA-256 of the normalized `BepInExRootPath`. App files, root `overlay-config.json`, and `timestamps/` are isolated per BepInEx profile. Settings UI `localStorage` remains in Electron's existing storage; browser-profile isolation is unchanged.

Close **both the game and desktop app** before updating or preparing migration. Only desktop AutoStart invokes migration; Native HUD timestamps are unchanged. A recognized legacy `OverlayHUD_app` beside the DLL is copied in full, including config, timestamps, and unknown files. Archive updates preserve the root config and timestamps. The legacy folder is deleted only after successful migration; do not delete it beforehand. If both legacy and external installs exist, the legacy folder is left untouched with a warning for manual review. Locked cleanup also produces a warning.

An archive SHA-256 change triggers a staged upgrade without manual deletion. Failed updates preserve the original app and abort launch. Relocation only needs the updated plugin DLL with the existing bundle, not an Electron rebuild.

Look for `Bundled OverlayHUD desktop app ready at:` in `BepInEx/LogOutput.log` for the actual executable path. `Extracted bundled OverlayHUD desktop app` now applies only to custom installs.

External files survive mod uninstall or profile deletion. With the app closed, back up needed timestamps and root config, then manually delete the corresponding `%LOCALAPPDATA%\OverlayHUD\profiles\<profile-key>` folder if no longer needed.

## Build and Package Both Variants

Requirements: Windows PowerShell 5.1 or PowerShell 7, .NET SDK targeting `net472`, installed R.E.P.O. Unity/game assemblies at the project's `HintPath` locations, and reference assemblies in `overlay-hud/lib/`. The recommended release build also requires Node.js, npm, and dependencies installed in `game-overlay/` (`npm install`). Reusing a verified current desktop bundle requires no Electron rebuild.

Run from the repository root. Rebuilding Electron is recommended for releases:

```powershell
powershell -NoProfile -File tools/Build-Packages.ps1 -RebuildDesktop
# Release rerun: explicitly replace previously generated outputs:
powershell -NoProfile -File tools/Build-Packages.ps1 -RebuildDesktop -Force
# Reuse the default bundle only when it fully matches the current release and sources:
powershell -NoProfile -File tools/Build-Packages.ps1
# Alternative current bundle (relative paths use the caller's working directory):
powershell -NoProfile -File tools/Build-Packages.ps1 -BundlePath "C:\build\OverlayHUD_app.zip"
```

`-RebuildDesktop` runs `npm.cmd run package` in `game-overlay/` and creates a fresh `OverlayHUD_app.zip` in staging. It does not overwrite the existing input ZIP. Install app dependencies beforehand; `-BundlePath` cannot be combined with `-RebuildDesktop`.

Without `-RebuildDesktop`, the input defaults to `overlay-hud/package-flat/OverlayHUD_app.zip`, or the custom `-BundlePath`. It must contain `OverlayHUD.exe` at its root. Both reused and freshly built bundles are validated: the app version must match the release, and SHA-256 hashes of web files, app files, and every file in `assets/` must match the current sources. Stale or missing content is rejected with rebuild guidance; updating only the DLL cannot fix an old Electron bundle. Existing input ZIPs and DLL copies in `package-flat/` and `package/plugins/` remain untouched.

Before installing the update, close the game and any running OverlayHUD Electron app. The changed bundle SHA-256 triggers the managed app update on the next desktop AutoStart, preserving the root config and timestamps.

### Build contract

- Builds Release twice, explicitly passing `-p:NativeOnly=false` and `-p:NativeOnly=true`.
- The project implements `NativeOnly=true` by defining `NATIVE_ONLY` and excluding `BundledAppStorage.cs`; it must exclude all `obj/`, `bin/`, and `dist/` trees from default compile globs, not just the active intermediate path.
- Both `BaseIntermediateOutputPath` and `MSBuildProjectExtensionsPath` are isolated under `obj/Full/<run-id>/` or `obj/NativeOnly/<run-id>/`. Restore state and generated assembly attributes cannot cross between variants or runs.
- Explicit `OutputPath` values point to separate `overlay-hud/dist/packages/<run-id>/build/` subdirectories. The script disables target-framework/runtime suffixes and reads the DLL directly from that variant's output.
- Staging files and checked archives are retained under `overlay-hud/dist/packages/<run-id>/`. Only explicitly named files enter the ZIP; build outputs, generated cfg files, PDBs and reference DLLs are excluded.
- Versions are read from the Full manifest and checked against the project and Native manifest. The script does not bump versions. Both manifest identities and dependencies must match.
- Both ZIPs are constructed and their exact entry names/counts checked before either is copied to the repository root. This is structural validation, not a substitute for DLL regression tests or a game session.
- Existing suffixed output archives cause a preflight failure unless `-Force` is passed. Even with `-Force`, `EgorSalad-OverlayHUD-26.11.2.zip` is never touched. SHA-256 hashes of each DLL and final archive are printed.

### Package contract

`EgorSalad-OverlayHUD-26.11.4-Full.zip` has exactly seven root entries:

```text
icon.png
manifest.json
OverlayHUD.dll
OverlayHUD_app.zip
README.md
LICENSE
THIRD_PARTY_NOTICES.md
```

`EgorSalad-OverlayHUD-26.11.4-Native.zip` has exactly six root entries: the same list without `OverlayHUD_app.zip`, using the separately compiled Native DLL and `package-native/` README, manifest and notices. Native notices include the complete font license without desktop runtime references. Full templates come from `package-flat/`; the icon and repository MIT license are shared. No directory entries or leading `./` records are added.

A normal development build remains `dotnet build overlay-hud/OverlayHUD.csproj -c Release` (Full). For releases use the packaging script rather than manually copying a potentially stale DLL between layouts.
