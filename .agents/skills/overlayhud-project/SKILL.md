---
name: overlayhud-project
description: Карта проекта OverlayHUD — неофициального HUD-оверлея для игры R.E.P.O. (BepInEx-плагин + Node.js сервер + Electron-приложение + HTML/CSS/JS UI). Используй этот скилл в начале любой задачи в этом репозитории, чтобы понять архитектуру, где что лежит, как компоненты общаются друг с другом и какие соглашения приняты в проекте.
---

# OverlayHUD — карта проекта

OverlayHUD — фанатский мод для игры **R.E.P.O.**, который рисует прозрачный
HUD-оверлей поверх игры (монстры, здоровье, таймер уровня, улучшения игрока,
стоимость ценностей на карте). Автор: EgorSalad. Полный README на русском —
`README.md` в корне репозитория, обязательно смотри его для деталей
(конфигурация, диагностика, версионирование, сборка).

## Архитектура (4 слоя)

```
R.E.P.O. (Unity)
   -> overlay-hud/Plugin.cs   (BepInEx + Harmony плагин, C#)
        -> HTTP JSON POST на 127.0.0.1:8787/api/...
   -> server.js                (Node.js, чистый http, без фреймворков)
        -> хранит overlayState, отдаёт его через Server-Sent Events (SSE)
   -> overlay.html + overlay.css + overlay-state.js  (клиентский UI, vanilla JS)
   -> game-overlay/main.js     (Electron: прозрачное click-through окно,
                                 встраивает и запускает тот же server.js)
```

Плагин на C# — единственный источник игровых событий. Он не показывает игроку
весь ростер уровня сразу: сначала строит внутренний ростер для синхронизации
статусов, а в видимую часть HUD монстр попадает только после того, как игра
зафиксировала визуальную встречу с ним (см. `EnemyOnScreen`/`PlayerVision` в
`Plugin.cs`).

## Где что лежит

- `overlay-hud/Plugin.cs` — весь BepInEx-плагин (один большой файл, ~3000+
  строк). Содержит: Harmony-патчи на события уровня/врагов/ценностей/улучшений,
  конфиг (`ConfigEntry<...>`), логику распаковки и запуска бандл-приложения,
  HTTP-клиент на основе `HttpWebRequest`/сырых сокетов (без внешних либ) для
  POST/GET на локальный сервер. Endpoints, которые плагин дёргает:
  `/api/monster-seen`, `/api/level`, `/api/roster`, `/api/monster-status`,
  `/api/visibility`, `/api/tab-hidden`, `/api/upgrades`, `/api/map-value`,
  `/api/state` (fallback).
- `overlay-hud/OverlayHUD.csproj` — таргет `net472`. Референсы на BepInEx,
  0Harmony и UnityEngine-модули берутся из `lib/` и из Steam-каталога игры
  (`HintPath` нужно менять под свою установку R.E.P.O.).
- `overlay-hud/lib/` — `BepInEx.dll`, `0Harmony.dll` (референсные сборки).
- `overlay-hud/stubs/BepInEx/` — сабсет BepInEx-типов, исключён из компиляции
  через `<Compile Remove="stubs\**\*.cs" />` в `.csproj` (нужен только для
  подсказок IDE/анализа, не участвует в сборке).
- `overlay-hud/package/` — layout Thunderstore-пакета с подкаталогами
  (`plugins/OverlayHUD/OverlayHUD.dll`), содержит `manifest.json`, `icon.png`,
  `README.md`, `THIRD_PARTY_NOTICES.md`.
- `overlay-hud/package-flat/` — "flat" layout для Gale/Thunderstore: файлы
  без подкаталогов (`OverlayHUD.dll`, `OverlayHUD_app.zip`, `manifest.json`,
  `icon.png`, `README.md`, `LICENSE`, `THIRD_PARTY_NOTICES.md`). Это то, что
  реально импортируется как мод.
- `overlay-hud/bin/`, `overlay-hud/obj/` — сборочные артефакты .NET, не
  хранятся в Git (см. `.gitignore`).
- `overlay-hud/README.md` — англоязычные build-notes для плагина.

- `server.js` — Node.js HTTP-сервер без фреймворков (`http`, `fs`, `path`).
  Хранит единственный объект `overlayState` в памяти, нормализует его через
  `normalizeOverlayState`, раздаёт статику (overlay.html/css/js, assets) и
  обрабатывает `/api/...` POST-эндпоинты от плагина. Обновления клиентам
  рассылаются через SSE (`broadcastState`, `text/event-stream`), а не через
  polling. По умолчанию слушает `HOST=0.0.0.0`, `PORT=8787` (через env),
  но в packaged Electron-приложении принудительно биндится на `127.0.0.1`.
  Также содержит конфиг монстров (`monsterConfig.levels/strength/aliases`) —
  дублирует те же данные, что и в `overlay-state.js` (держи в синхроне при
  правках!).
- `overlay.html` — разметка HUD и панели настроек + весь клиентский JS одним
  файлом (inline `<script>`). Основные функции: `render()` — главный цикл
  перерисовки, `updateMonsterSquare()`/`ensureMonsterSquares()` — плитки
  монстров, `applyViewportScale()`/`positionElement()`/`snapPosition()` —
  масштабирование и drag&drop/снаппинг окна, `applyInterfaceLanguage()` —
  переключение ru/en, `buildUpgradeRow()`/`layoutUpgradeRow()` — ряд улучшений.
- `overlay.css` — стили и анимации HUD.
- `overlay-state.js` — общий модуль `OverlayApp` (IIFE), используется и на
  странице оверлея, и в настройках. Отвечает за localStorage, синхронизацию
  между вкладками через `BroadcastChannel`, и через SSE-клиент к серверу
  (`serverSyncEnabled` — только когда страница открыта по http/https, не
  file://). Содержит те же таблицы монстров/уровней, что и `server.js`.
- `start-server.bat` — ручной запуск `server.js` для локальной разработки
  (без Electron), слушает на всех интерфейсах по умолчанию.

- `game-overlay/main.js` — Electron-приложение: создаёт прозрачное
  click-through окно, встраивает и запускает `server.js`, открывает
  `http://127.0.0.1:8787/overlay.html`. Поддерживает трей-иконку и глобальные
  хоткеи (`Ctrl+Alt+O/I/R/Q`).
- `game-overlay/overlay-config.json` — `overlayUrl`, `displayIndex` (монитор,
  0-based), `zoomFactor`.
- `game-overlay/package.json` — скрипты: `npm start` (`electron .`),
  `npm run package` (`electron-packager`, кладёт в `dist/`, тащит с собой
  `server.js`, `overlay.html/css`, `overlay-state.js`, `assets/` как
  `extraResource`).
- `game-overlay/build-portable.bat` — обёртка для сборки portable-версии.
- `game-overlay/start-game-overlay.bat` — запуск на игровом ПК (ставит
  Electron при первом запуске).
- `game-overlay/dist/`, `node_modules/`, `.electron-cache/` — генерируются
  локально, не в Git.

- `assets/monsters/*.webp` — иконки монстров (имена файлов — snake_case или
  с пробелами, см. `getMonsterFileName()` в `server.js`/`overlay-state.js`
  для маппинга игровых имён на файлы).
- `assets/upgrades/*.webp` — иконки улучшений игрока.
- `assets/icons/icon.ico`, `icon.png` — иконка приложения/трея.
- `assets/fonts/` — шрифты Roboto и Teko (OFL 1.1).
- `docs/images/` — скриншоты для README.

## Ключевые соглашения

- **Единый источник правды по монстрам** — таблицы `monsterConfig` в
  `server.js` и `overlay-state.js` должны совпадать. При добавлении/правке
  монстра меняй оба файла.
- **Порт и хост**: продовое значение всегда `127.0.0.1:8787`. `0.0.0.0`
  используется только при ручном запуске `start-server.bat` для разработки.
- **Версионирование**: схема `YY.M.Patch` (например `26.8.0`), совпадает в
  `Plugin.cs` (`[BepInPlugin(...)]`), `manifest.json` в `package/` и
  `package-flat/`, и в заголовке README. При релизе проверяй все три места.
- **Flat-пакет для Gale** должен содержать ровно: `icon.png`,
  `manifest.json`, `OverlayHUD.dll`, `OverlayHUD_app.zip`, `README.md`,
  `LICENSE`, `THIRD_PARTY_NOTICES.md` — ничего лишнего.
- **`OverlayHUD_app.zip`** — архив Electron-приложения, который плагин
  распаковывает рядом с DLL в `OverlayHUD_app/` при первом запуске. При
  архивации перечисляй верхнеуровневые файлы явно (не `tar -C <dir> .`,
  это добавляет лишнюю запись `./`).
- **Детекция монстров** событийная (Harmony-патчи), периодические проходы
  — только страховка (`Detection.StatusIntervalSeconds`, минимум 10 сек).
- Комментарии/README пишутся по-русски (основной README), но код и
  внутренний build-notes README в `overlay-hud/` и `game-overlay/` — на
  английском.

## Типичные точки входа для задач

- **Изменить логику детекции монстров/уровней в игре** →
  `overlay-hud/Plugin.cs`.
- **Изменить данные о монстрах (сила, уровень, алиасы, файлы иконок)** →
  править синхронно в `server.js` и `overlay-state.js`.
  и `assets/monsters/`.
  - **Изменить внешний вид/поведение HUD или панели настроек** →
    `overlay.html` (разметка+JS), `overlay.css` (стили), `overlay-state.js`
    (состояние/persist/sync).
  - **Изменить поведение Electron-окна (хоткеи, трей, монитор)** →
    `game-overlay/main.js`, `game-overlay/overlay-config.json`.
  - **Новый API endpoint между плагином и сервером** → добавить обработчик в
    `server.js`, вызов — в `Plugin.cs` (метод-помощник `SendHttpPost`/
    `BuildSiblingEndpoint`), при необходимости — обработку состояния в
    `overlay-state.js`/`overlay.html`.
  - **Сборка/упаковка релиза** → см. разделы README «Сборка из исходников» и
    «Gale/Thunderstore flat package»; команды: `dotnet build
    overlay-hud/OverlayHUD.csproj -c Release`, `cd game-overlay && npm install
    && npm run package`.

## Диагностика

Основной источник — `BepInEx/LogOutput.log` игрового профиля. Ключевые строки
для grep: `Loading [OverlayHUD`, `Extracted bundled OverlayHUD desktop app`,
`Started bundled OverlayHUD desktop app`, `Registered spawned enemy parent`,
`Marked monster for web overlay`, `HTTP/1.1 200 OK`. Подробный чек-лист — в
README, раздел «Диагностика».
