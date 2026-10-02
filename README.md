# SC NEXUS

[English](#english) · [Русский](#русский)

## English

SC NEXUS is a Windows 10/11 companion for Star Citizen. It tracks the game without modifying its process, recommends trade routes and ship loadouts, keeps a fleet and trip history, and minimizes manual input.

### Install

Download the [latest release](https://github.com/aelz1233/sc-nexus/releases/latest):

- **SCNexus-Setup-…exe** — regular installer with a selectable installation folder;
- **SCNexus-…win-x64.zip** — portable build.

.NET is bundled. Updates preserve the fleet, balance, settings, and trip history in `%LOCALAPPDATA%\SCNexus`.

### Automatic data collection

Every collected value includes its source, timestamp, and confidence. Providers are evaluated in this order:

1. `Game.log` and `logbackups` using an incremental real-time tail;
2. local Star Citizen files in read-only mode;
3. UEX and Star Citizen Wiki APIs;
4. optional OCR of the foreground Star Citizen window;
5. saved Nexus history;
6. manual values only when no automatic source is available.

The app automatically finds LIVE, PTU, or EPTU on first launch. Manual folder selection appears only if detection fails. The Tools page shows values in a form such as `Orison • Game.log • just now`, reports the status and last successful update of every provider, and marks stale values as last known.

Depending on what the current game build exposes, SC NEXUS records the game build and environment, sessions and shard, locations, structured mission/objective changes, trade requests, balance lines, detected ships, movement history, market freshness, and component catalog coverage. Reliable detections are saved to the local SQLite history. A detected ship is added to the fleet only after an exact match with the UEX catalog.

OCR is disabled by default. When enabled, it captures only the foreground Star Citizen client, keeps the image in memory while extracting supported values, and releases it immediately after recognition.

SC NEXUS never uses DLL injection, process memory reading, hooks, packet interception, or changes to Star Citizen files.

### Features

**Trade routes.** Select a ship and budget, or let automatic data fill what is available. Direct routes, trade chains, and multi-stop cargo collection use current UEX prices, stock, and demand. Pyro routes are marked dangerous. When a multi-stop plan is selected, route guidance advances from detected locations and matching trade events in `Game.log`, persists across restarts, and still allows a manual step correction when the log is incomplete. Completed trips add personal travel time and estimated profit per hour to matching route cards.

**Fleet and loadouts.** Add ships from the UEX catalog or let reliable detections add them. The loadout planner is embedded in the Ships page and uses compatible ports and component stats from Star Citizen Wiki plus store prices from UEX. It produces the best build within a budget and an unrestricted build, compares power generation, component power draw, cooling, quantum speed, fuel consumption, and estimated range, then builds a component shopping route. The planner minimizes store stops among offers no more than 5% above each component's lowest price, shows the exact premium, prefers the current location, current system, and safe locations, and places Pyro last.

**External game overlay.** An optional compact or expanded click-through window shows the detected ship and location, active route stop, cargo, expected profit, mission, data freshness, and Pyro/NQA warnings. It is controlled from Nexus settings and appears automatically only while Star Citizen is running. Click the shortcut field and press any available keyboard combination; none is assigned by default. It does not inject into the game or add Windows startup entries.

**Notification area.** Closing the main window keeps monitoring active and minimizes SC NEXUS to the system tray. Double-click the icon to restore the window, or use its menu to open the app or exit completely.

**Journal and history.** Missions, deaths, movement, and trade requests detected in `Game.log` appear in one journal. Trade requests can fill actual purchase and sale amounts. Manual correction remains available. Statistics can be filtered by time and exported to CSV.

**Diagnostics and data safety.** The read-only PC check reports the game executable, build, GPU and VRAM from `Game.log`, memory, page file, disk space, launcher state, caches, and known recent crash signatures. Local application errors are written to a rotating log. Nexus creates one verified database backup per day and keeps the latest seven copies.

**Updates.** Settings → Check for updates downloads, verifies, and silently installs the latest GitHub release. Public releases need no GitHub token.

### Objective limitations

Star Citizen does not reliably expose the complete account fleet, current installed loadout, personal inventory, blueprints, exact wallet balance, or every mission/death/location event in public local logs. SC NEXUS records these only when a reliable log event, supported API value, or enabled OCR result exists. It does not invent missing values. UEX data is community supplied, so verify price, stock, and demand at the in-game terminal before buying.

### Build from source

Windows and the .NET 10 SDK are required:

```powershell
dotnet run --project SCNexus/SCNexus.csproj
dotnet test SCNexus.slnx -c Release
```

## Русский

SC NEXUS — компаньон Star Citizen для Windows 10/11. Он следит за игрой без вмешательства в её процесс, подбирает торговые маршруты и конфигурации кораблей, ведёт флот и историю рейсов и сводит ручной ввод к минимуму.

### Установка

Скачайте [последнюю версию](https://github.com/aelz1233/sc-nexus/releases/latest):

- **SCNexus-Setup-…exe** — обычная установка с выбором папки;
- **SCNexus-…win-x64.zip** — запуск без установки.

.NET уже включён. При обновлении флот, баланс, настройки и история рейсов сохраняются в `%LOCALAPPDATA%\SCNexus`.

### Автоматический сбор данных

Для каждого значения сохраняются источник, время и уверенность. Источники используются по приоритету:

1. `Game.log` и `logbackups` с инкрементальным отслеживанием новых строк;
2. локальные файлы Star Citizen только в режиме чтения;
3. API UEX и Star Citizen Wiki;
4. необязательный OCR окна Star Citizen;
5. накопленная история Nexus;
6. ручные значения, только когда автоматического источника нет.

При первом запуске программа сама ищет LIVE, PTU или EPTU. Ручной выбор папки появляется только при неудачном поиске. В «Инструментах» значения показаны в формате `Orison • Game.log • только что`, рядом видны состояние и время последнего успешного обновления каждого источника. Устаревшие значения помечаются как последние известные.

В зависимости от данных текущей версии игры SC NEXUS сохраняет build и контур игры, сессии и shard, локации, структурированные изменения миссий и objectives, торговые запросы, строки баланса, обнаруженные корабли, историю перемещений, свежесть рынка и покрытие каталога компонентов. Надёжные результаты сохраняются в локальную SQLite-базу. Обнаруженный корабль добавляется во флот только после точного совпадения с каталогом UEX.

OCR по умолчанию выключен. После включения он снимает только находящееся на переднем плане окно Star Citizen, держит изображение в памяти во время распознавания и сразу освобождает его. Временный файл на диске не создаётся.

SC NEXUS не использует DLL injection, чтение памяти процесса, hooks, перехват пакетов и не изменяет файлы Star Citizen.

### Возможности

**Торговые маршруты.** Выберите корабль и бюджет либо используйте автоматически найденные значения. Прямые рейсы, цепочки и сбор груза с нескольких остановок используют цены, stock и demand UEX. Маршруты через Pyro помечаются опасными. После выбора многоэтапного плана ведение само переключает остановки по обнаруженной локации и подходящим торговым событиям `Game.log`, сохраняется между запусками и допускает ручную коррекцию шага, если журнал не дал нужного события. Завершённые рейсы добавляют в карточки личное время перелёта и расчёт прибыли в час.

**Корабли и конфигуратор.** Добавляйте корабли из каталога UEX или используйте надёжное автоопределение. Конфигуратор встроен в экран кораблей, получает совместимые порты и характеристики из Star Citizen Wiki, а цены магазинов — из UEX. Он показывает лучший вариант в пределах бюджета и без ограничения суммы, сравнивает выработку и расход энергии, охлаждение, скорость квантового привода, расход топлива и расчётную дальность, затем строит маршрут закупки. Планировщик сокращает число магазинов среди предложений с переплатой не более 5% за каждую деталь, показывает точную переплату, предпочитает текущую локацию, текущую систему и безопасные точки, а Pyro ставит в конец.

**Внешний игровой оверлей.** Необязательное компактное или расширенное окно показывает обнаруженный корабль и локацию, текущую остановку маршрута, груз, ожидаемую прибыль, миссию, свежесть данных и предупреждения Pyro/NQA. Оверлей включается в настройках Nexus и появляется автоматически только при запущенном Star Citizen. Нажмите поле бинда и введите любое свободное сочетание клавиш; по умолчанию оно не назначено. Оверлей не внедряется в игру и не добавляет приложение в автозапуск Windows.

**Системный трей.** Крестик сохраняет мониторинг и сворачивает SC NEXUS в область уведомлений. Двойной щелчок по значку возвращает окно, а меню значка позволяет открыть приложение или полностью выйти.

**Журнал и история.** Миссии, смерти, перемещения и торговые запросы из `Game.log` собраны на одном экране. Торговые запросы могут автоматически заполнить фактические покупки и продажи. Поля разрешено исправлять вручную. Статистика фильтруется по времени и экспортируется в CSV.

**Диагностика и защита данных.** Проверка компьютера только читает сведения об исполняемом файле и build игры, GPU и VRAM из `Game.log`, памяти, файле подкачки, свободном месте, лаунчере, кэшах и известных свежих сигнатурах вылетов. Необработанные ошибки Nexus записываются в локальный циклический журнал. Раз в день создаётся проверенная копия базы; хранятся последние семь копий.

**Обновление.** Настройки → «Проверить обновление» скачивает, проверяет и тихо устанавливает последний GitHub-релиз. Для открытого репозитория токен не нужен.

### Объективные ограничения

Star Citizen не предоставляет в открытых локальных данных надёжный полный список кораблей аккаунта, фактически установленный loadout, личный инвентарь, blueprints, точный баланс и каждое событие миссии, смерти или перемещения. SC NEXUS записывает их только при наличии надёжной строки журнала, поддерживаемого значения API или результата включённого OCR. Отсутствующие данные не выдумываются. Данные UEX заполняются сообществом, поэтому перед закупкой проверяйте цену, запас и спрос в игровом терминале.

### Сборка из исходников

Нужны Windows и .NET 10 SDK:

```powershell
dotnet run --project SCNexus/SCNexus.csproj
dotnet test SCNexus.slnx -c Release
```
