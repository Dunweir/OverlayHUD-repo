# Repo Monster Bridge

Sends encountered R.E.P.O. monsters, level changes, player upgrades, and respawn cooldowns to the overlay server.

Holding Tab hides the game overlay until the key is released.

Peeper uses a separate detection distance only while the game marks it as very close to the player.

Respawn cooldown values are read from the game's current `EnemyDirector.enemiesSpawned` list and each enemy parent's `DespawnedTimer`.

Default endpoint:

```text
http://192.168.1.198:8787/api/monster-seen
```
