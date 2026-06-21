# Repo Monster Bridge

Sends encountered R.E.P.O. monsters, level changes, player upgrades, and respawn cooldowns to the overlay server.

Holding Tab hides the game overlay until the key is released.

When TimerMod is installed, multiplayer cooldown values are read from its synchronized timer data. The bridge falls back to the game's local `EnemyParent.DespawnedTimer` value.

Default endpoint:

```text
http://192.168.1.198:8787/api/monster-seen
```
