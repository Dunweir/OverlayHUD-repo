# Changelog

## 26.11.7

- Added a personal detection bar beneath monster HP. Blue shows progress toward detection. Red pulses while the monster sees you or continues tracking you during a chase. Both host and client need the update for multiplayer detection.
- Monster HP hides during despawn. With encounter-based reveals disabled, the tracker shows the known roster on level entry, including inactive monsters.
- Fixed Hauler loss tracking. Loading valuables preserves their value. Destroying a loaded Hauler counts its cargo once.
- Renamed the config file to `OverlayHUD.cfg`, removed section numbers and renamed `RequireLineOfSight` to `RevealMonstersOnEncounter`. Existing settings migrate on startup.
- Timestamp recording defaults to off. Saved preferences still apply.

## 26.11.6

- Updated the mod icon and centred Gnome artwork on its tile.

## 26.11.5

- Updated 30 monster icons. Enable `LegacyIcons` in `[Appearance]` to use the original artwork.
