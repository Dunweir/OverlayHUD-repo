# OverlayHUD — Native

**An in-game HUD for R.E.P.O. / Информационный HUD внутри R.E.P.O.**

[English](#english) · [Русский](#русский)

## English

Keep track of your run without leaving the game. OverlayHUD displays encountered monsters, player upgrades and valuable statistics directly inside R.E.P.O., with a customizable layout and optional visual effects.

This is the **Native edition**: the HUD is rendered through Unity, with its fonts and icons bundled into the plugin. No separate application or window is required.

### Features

- **Monster information** — health bars, required grab strength, grouped counts, respawn timers and circular respawn indicators.
- **Run overview** — current level, elapsed time, remaining valuable value and losses.
- **Player upgrades** — icons for owned upgrades, with individual visibility controls.
- **Visual feedback** — proximity waves, floating damage numbers, animations and optional destroyed-item notifications showing the last holder.
- **Flexible layout** — horizontal or grid layouts, alignment, offsets, scale, icon sizes, rows and columns.
- **Russian and English** — localized interface and official game names in tooltips.
- **Session timestamps** — local text logs of level starts, maps and encountered monsters.

By default, monsters are revealed **only after an encounter**. Undetected monsters remain placeholders, and receiving multiplayer data does not reveal their identities. The HUD hides while the map or quota counter is shown.

### Customization

Settings are grouped by purpose in `BepInEx/config/local.overlay.overlay_hud.cfg`: detection, general HUD options, layout, information, monsters, effects and individual upgrades.

Native HUD appearance settings can be changed live through **BepInEx Configuration Manager**, if available. Manual file edits require a config reload or game restart.

- Choose `Language = en` or `ru` in **03 Native - General**; Russian is the default.
- Show or hide individual HUD elements without stopping game-state tracking.
- For hover tooltips, enable `ShowTooltips` and disable `UseGameUIEffects` in **07 Native - Effects**. Tooltips work when the game cursor is unlocked; the HUD does not unlock or capture it.
- Timestamps are saved to `BepInEx/plugins/OverlayHUD/timestamps/` and can be disabled with `SaveTimestamps`.

### Multiplayer and limitations

- Accurate client-side respawn timers and health synchronization require compatible, current OverlayHUD versions on the host and clients. Health synchronization is experimental.
- Damage numbers show a monster's health loss from all sources, not an individual player's damage score.
- Game UI effects are experimental and may reduce text sharpness. Disable `UseGameUIEffects` for independent, sharper rendering.
- Monsters without a dedicated icon use a placeholder. Unsupported font characters appear as `?`.
- This is an alternative edition of the same plugin, not an add-on for another OverlayHUD edition. Use only one copy per profile.

### Feedback

For bug reports, include `BepInEx/LogOutput.log`, your config and a short description of the issue. Optional detailed logging is available under **99 Diagnostics**.

[Report an issue](https://github.com/Dunweir/OverlayHUD-repo/issues) · [Source code and detailed documentation](https://github.com/Dunweir/OverlayHUD-repo)

---

## Русский

Следите за забегом, не выходя из игры. OverlayHUD показывает встреченных монстров, улучшения игрока и статистику ценностей прямо внутри R.E.P.O. Расположение, состав HUD и визуальные эффекты можно настроить под себя.

Это **Native-версия**: HUD рисуется средствами Unity, а шрифты и иконки встроены в плагин. Отдельное приложение или окно не требуется.

### Возможности

- **Информация о монстрах** — полоски здоровья, требуемая сила захвата, количество в группе, таймеры и круговые индикаторы респавна.
- **Состояние забега** — текущий уровень, время прохождения, оставшаяся стоимость ценностей и потери.
- **Улучшения игрока** — иконки полученных улучшений с отдельными переключателями видимости.
- **Визуальные подсказки** — волны близости, всплывающие числа урона, анимации и включаемые уведомления об уничтоженных предметах с именем последнего державшего их игрока.
- **Гибкое расположение** — горизонтальный HUD или сетка, выравнивание, смещения, масштаб, размеры иконок, ряды и колонки.
- **Русский и английский языки** — локализованный интерфейс и официальные игровые названия в подсказках.
- **Таймкоды сессии** — локальные текстовые записи начала уровней, карт и встреченных монстров.

По умолчанию монстры раскрываются **только после встречи**. Необнаруженные виды остаются заглушками, а получение данных в мультиплеере не раскрывает их названия. HUD скрывается при просмотре карты и показе счётчика квоты.

### Настройка

Параметры находятся в `BepInEx/config/local.overlay.overlay_hud.cfg` и сгруппированы по назначению: обнаружение, общие настройки HUD, расположение, информация, монстры, эффекты и отдельные улучшения.

Внешний вид Native HUD можно менять на лету через **BepInEx Configuration Manager**, если он доступен. После ручного редактирования файла требуется перезагрузить конфиг или игру.

- Язык выбирается параметром `Language = ru` или `en` в **03 Native - General**; по умолчанию — русский.
- Отдельные элементы HUD можно скрывать, не останавливая отслеживание состояния игры.
- Для подсказок при наведении включите `ShowTooltips` и отключите `UseGameUIEffects` в **07 Native - Effects**. Подсказки работают при разблокированном игровом курсоре; сам HUD не разблокирует и не перехватывает его.
- Таймкоды сохраняются в `BepInEx/plugins/OverlayHUD/timestamps/`; запись отключается параметром `SaveTimestamps`.

### Мультиплеер и ограничения

- Для точных таймеров респавна у клиента и синхронизации ХП нужны совместимые актуальные версии OverlayHUD у хоста и клиентов. Синхронизация ХП экспериментальная.
- Числа урона показывают потерю здоровья монстра от всех источников, а не личный урон игрока.
- Эффекты игрового интерфейса экспериментальные и могут снижать чёткость текста. Отключите `UseGameUIEffects` для независимого, более чёткого вывода.
- Монстры без собственной иконки отображаются с заглушкой. Неподдерживаемые шрифтом символы заменяются на `?`.
- Это альтернативная версия того же плагина, а не дополнение к другой сборке OverlayHUD. В профиле должна быть только одна копия.

### Обратная связь

К сообщению об ошибке приложите `BepInEx/LogOutput.log`, конфиг и краткое описание проблемы. Подробные логи можно включить в секции **99 Diagnostics**.

[Сообщить об ошибке](https://github.com/Dunweir/OverlayHUD-repo/issues) · [Исходники и подробная документация](https://github.com/Dunweir/OverlayHUD-repo)

---

**Author / Автор: EgorSalad**

Unofficial fan project; not affiliated with semiwork, the publisher of R.E.P.O. or Thunderstore.

Неофициальный фанатский проект; не связан с semiwork, издателем R.E.P.O. или Thunderstore.

Code / Код: **MIT**. Fonts and game artwork retain their respective licenses and rights; see `LICENSE` and `THIRD_PARTY_NOTICES.md`.

Шрифты и игровые изображения сохраняют собственные лицензии и права; см. `LICENSE` и `THIRD_PARTY_NOTICES.md`.
