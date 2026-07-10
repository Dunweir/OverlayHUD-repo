# OverlayHUD

Small BepInEx plugin source for sending encountered R.E.P.O. enemies to the local stream overlay.

The plugin does not reveal the whole level list. It scans from the active camera and sends a monster only when an enemy-like Unity object is in view, close enough, and optionally has line of sight. Peeper has an extra distance limit only while the game marks it as very close to the player.

## Overlay Endpoint

The overlay server accepts:

```http
POST http://192.168.1.198:8787/api/monster-seen
Content-Type: application/json

{"name":"Spewer"}
```

The server normalizes the name, applies the current level slot limits, skips already-known monster types, and broadcasts the updated state to `overlay.html`.

The bridge also posts per-enemy alive/dead state and `DespawnedTimer` values to `/api/monster-status` by reading the game's current `EnemyDirector.enemiesSpawned` list.

The bridge synchronizes local Strength, Tumble Launch, Range, Sprint Speed, Tumble Wings, Crouch Rest, Extra Jump, and Tumble Climb upgrades from `StatsManager`.

Upgrade synchronization patches each concrete item `Upgrade()` method and reads only the changed value, avoiding both recurring polls and a full eight-upgrade scan on consumption. Holding Tab is sent to the overlay as a temporary hide state.

## Build Notes

1. Install BepInEx for R.E.P.O. through r2modman/Thunderstore or manually.
2. Copy the needed DLL references into `../lib/` or change `OverlayHUD.csproj` `HintPath` values to point at your game install.
3. Build with `dotnet build -c Release`.
4. Copy `bin/Release/net472/OverlayHUD.dll` into the R.E.P.O. BepInEx `plugins` folder.
5. Start this overlay server before launching/entering a run:

```powershell
node server.js
```

The server listens on all local network interfaces by default. From the gaming PC, the default endpoint is:

```text
http://192.168.1.198:8787/api/monster-seen
```

After first launch, BepInEx creates a config file where distance, Peeper distance, cooldown, endpoint, and line-of-sight behavior can be tuned.
