# REPO Monster Bridge

Small BepInEx plugin source for sending encountered R.E.P.O. enemies to the local stream overlay.

The plugin does not reveal the whole level list. It scans from the active camera and sends a monster only when an enemy-like Unity object is in view, close enough, and optionally has line of sight.

## Overlay Endpoint

The overlay server accepts:

```http
POST http://192.168.1.198:8787/api/monster-seen
Content-Type: application/json

{"name":"Spewer"}
```

The server normalizes the name, applies the current level slot limits, skips already-known monster types, and broadcasts the updated state to `control.html` and `overlay.html`.

## Build Notes

1. Install BepInEx for R.E.P.O. through r2modman/Thunderstore or manually.
2. Copy the needed DLL references into `../lib/` or change `RepoMonsterBridge.csproj` `HintPath` values to point at your game install.
3. Build with `dotnet build -c Release`.
4. Copy `bin/Release/net472/RepoMonsterBridge.dll` into the R.E.P.O. BepInEx `plugins` folder.
5. Start this overlay server before launching/entering a run:

```powershell
node server.js
```

The server listens on all local network interfaces by default. From the gaming PC, the default endpoint is:

```text
http://192.168.1.198:8787/api/monster-seen
```

After first launch, BepInEx creates a config file where distance, cooldown, endpoint, and line-of-sight behavior can be tuned.
