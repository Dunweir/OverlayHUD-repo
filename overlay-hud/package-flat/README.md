# OverlayHUD

**Автор: EgorSalad**

OverlayHUD добавляет в R.E.P.O. настольный HUD поверх окна игры. Мод состоит из BepInEx-плагина `OverlayHUD.dll` и комплектного приложения `OverlayHUD.exe`.

При первом запуске плагин распаковывает `OverlayHUD.exe` из архива мода в папку `OverlayHUD_app` рядом с DLL, а затем автоматически запускает его вместе с игрой. Плагин получает игровые события и передаёт их EXE через локальный адрес `127.0.0.1`; данные не отправляются во внешний интернет. При выходе из игры приложение автоматически закрывается.

HUD отображает:

- встреченных монстров и их силу;
- здоровье монстров и таймеры респавна;
- номер уровня и таймер;
- улучшения игрока;
- текущую стоимость ценностей на карте и сумму потерь.

Монстры появляются только после встречи и не раскрывают весь состав уровня заранее. Удержание Tab временно скрывает HUD. В оверлее доступны настройки расположения, масштаба, количества колонок, видимости элементов, языка и другие параметры отображения.

Для работы нужен BepInEx в активном профиле Gale. Electron, Node.js и сам EXE отдельно устанавливать или запускать не требуется. Чтобы прозрачный HUD отображался поверх игры, используйте оконный или borderless-режим вместо exclusive fullscreen.

## English

**Author: EgorSalad**

OverlayHUD adds a desktop HUD over the R.E.P.O. game window. The mod consists of the `OverlayHUD.dll` BepInEx plugin and the bundled `OverlayHUD.exe` application.

On first launch, the plugin extracts `OverlayHUD.exe` from the mod archive into an `OverlayHUD_app` folder next to the DLL, then starts it automatically with the game. The plugin receives game events and sends them to the EXE through the local `127.0.0.1` address; no data is sent to the external internet. The app closes automatically when the game exits.

The HUD displays:

- encountered monsters and their strength;
- monster health and respawn cooldowns;
- level number and timer;
- player upgrades;
- current map value and lost value.

Monsters appear only after an encounter instead of revealing the entire level roster. Holding Tab temporarily hides the HUD. The overlay includes controls for layout, scale, columns, visibility, language, and other display options.

BepInEx must be installed in the active Gale profile. Electron, Node.js, and the EXE do not need to be installed or launched separately. Use windowed or borderless mode instead of exclusive fullscreen so the transparent HUD can appear over the game.

## License

OverlayHUD source code is licensed under the MIT License. See `LICENSE` and `THIRD_PARTY_NOTICES.md` included with the package.
