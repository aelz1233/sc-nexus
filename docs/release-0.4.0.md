# SC NEXUS 0.4.0

## English

This release adds live route guidance, an external game overlay, and a more complete ship loadout workflow.

- Added an optional compact or expanded click-through overlay with the detected ship and location, active route stop, cargo, expected profit, mission, source freshness, and Pyro/NQA warnings.
- The overlay is controlled from Nexus settings and appears automatically only while Star Citizen is running. An optional show/hide shortcut can be selected; none is assigned by default. No Windows startup entry is created.
- Added persistent multi-stop guidance. Nexus advances stops from detected locations and matching purchase/sale events in `Game.log`; manual previous/next controls remain available when the game log is incomplete.
- Expanded the ship configurator with component power draw, power generation, coolant demand and generation, quantum speed, fuel consumption, and estimated quantum range.
- Added a component shopping route grouped by system, location, store, item, and quantity. It minimizes store stops among offers up to 5% above each component's lowest price, shows the premium, prefers the current location, current system, and safe locations, and puts Pyro last.
- Versioned the component cache so older cached catalogs are refreshed when new measured fields become available.
- Added settings migration, Russian and English interface text, persistence across restarts, and regression tests for route progression, engineering calculations, shopping order, and old databases.

The overlay remains a separate read-only Windows window. SC NEXUS still uses no DLL injection, process memory reading, hooks, packet interception, or modification of Star Citizen files.

## Русский

В этом выпуске появились ведение по маршруту, внешний игровой оверлей и расширенный конфигуратор кораблей.

- Добавлен необязательный компактный или расширенный оверлей без перехвата кликов. Он показывает корабль и локацию, текущую остановку, груз, ожидаемую прибыль, миссию, свежесть источников и предупреждения Pyro/NQA.
- Оверлей управляется из настроек Nexus и появляется автоматически только при запущенном Star Citizen. Можно назначить бинд показа и скрытия; по умолчанию он выключен. Добавления в автозапуск Windows нет.
- Добавлено сохраняемое ведение по многоэтапному маршруту. Nexus переключает остановки по обнаруженной локации и подходящим покупкам/продажам из `Game.log`; при неполном журнале шаг можно исправить вручную.
- Конфигуратор дополнен расходом и выработкой энергии, потребностью и запасом охлаждения, скоростью квантового привода, расходом топлива и расчётной дальностью.
- Добавлен маршрут покупки компонентов по системам, локациям, магазинам, деталям и количеству. Он сокращает число магазинов среди предложений с переплатой до 5% за деталь, показывает переплату, предпочитает текущую локацию, текущую систему и безопасные точки, а Pyro ставит в конец.
- Кэш компонентов получил версию схемы: старые сохранённые каталоги автоматически обновляются для загрузки новых измеримых полей.
- Добавлены миграция настроек, русские и английские тексты, сохранение состояния между запусками и регрессионные тесты переходов по маршруту, инженерных расчётов, порядка магазинов и старых баз.

Оверлей остаётся отдельным внешним окном только для чтения. SC NEXUS по-прежнему не использует DLL injection, чтение памяти процесса, hooks, перехват пакетов и изменение файлов Star Citizen.
