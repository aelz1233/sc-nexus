using System.Collections;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace SCNexus.Services;

public static class LocalizationService
{
    private static readonly Dictionary<string, string> English = new(StringComparer.Ordinal)
    {
        ["ЛИЧНЫЙ КОМПАНЬОН"] = "PERSONAL COMPANION", ["НА ЭТОМ КОМПЬЮТЕРЕ"] = "ON THIS PC",
        ["Локальные настройки"] = "Local settings", ["Обзор"] = "Dashboard", ["Маршруты"] = "Routes",
        ["Флот"] = "Fleet", ["Конфигуратор"] = "Loadout", ["Рейсы"] = "Trips",
        ["Корабли"] = "Ships", ["Конфигуратор оснащения"] = "Loadout configurator",
        ["Журнал"] = "Journal", ["Автоматический журнал игры"] = "Automatic game journal",
        ["События появляются из Game.log автоматически; неподтверждённые данные Nexus не придумывает."] = "Events appear from Game.log automatically; Nexus does not invent unconfirmed data.",
        ["События игры, текущий рейс и история"] = "Game events, current trip, and history",
        ["Инструменты"] = "Tools", ["Настройки"] = "Settings", ["Мой флот"] = "My fleet",
        ["История рейсов"] = "Trip history", ["Параметры рейса"] = "Route settings",
        ["Подходящие маршруты"] = "Matching routes", ["Фильтры и ограничения"] = "Filters and limits",
        ["Корабль"] = "Ship", ["КОРАБЛЬ"] = "SHIP", ["СОРТИРОВКА"] = "SORTING", ["ТРЮМ"] = "CARGO HOLD",
        ["Баланс, aUEC"] = "Balance, aUEC", ["Денежный резерв, aUEC"] = "Cash reserve, aUEC",
        ["Режим планирования"] = "Planning mode", ["Системы маршрута"] = "Route systems",
        ["Минимум заполнения, %"] = "Minimum fill, %", ["Минимум прибыли, aUEC"] = "Minimum profit, aUEC",
        ["Только рейсы внутри одной системы"] = "Only routes within one system", ["Включить терминалы NQA"] = "Include NQA terminals",
        ["Исключить Pyro"] = "Exclude Pyro", ["Выбрать все"] = "Select all", ["Сбросить фильтры"] = "Reset filters",
        ["Обновить цены"] = "Refresh prices", ["Подобрать маршрут  →"] = "Find a route  →", ["Подобрать рейсы"] = "Build routes",
        ["Любая локация"] = "Any location", ["Показать ещё 30"] = "Show 30 more", ["Выбрать этот рейс"] = "Use this route",
        ["Перенести в рейс"] = "Send to trip", ["Остановки и груз"] = "Stops and cargo",
        ["До точек закупки"] = "Purchase stops", ["Конечная точка · поиск по системе и названию"] = "Destination · search by system or name",
        ["Сохранить план в файл"] = "Save plan to file", ["МОЙ ФЛОТ"] = "MY FLEET", ["Добавить первый корабль"] = "Add your first ship",
        ["Добавить корабль"] = "Add ship", ["Поиск по каталогу"] = "Catalog search", ["Каталог UEX"] = "UEX catalog",
        ["МОДЕЛЬ"] = "MODEL", ["ГРУЗОВОЙ ОБЪЁМ"] = "CARGO CAPACITY", ["РОЛЬ"] = "ROLE",
        ["СБОРКА И ЗАМЕТКИ"] = "LOADOUT AND NOTES", ["Удалить"] = "Delete", ["Выбрать для рейсов"] = "Use for routes",
        ["КОРАБЛЬ ДЛЯ НОВОГО РЕЙСА"] = "SHIP FOR NEW TRIP", ["ОТКУДА"] = "FROM", ["КУДА"] = "TO",
        ["ТОВАР"] = "COMMODITY", ["ВЛОЖЕНО"] = "INVESTED", ["ПОЛУЧЕНО"] = "RECEIVED",
        ["РАСХОДЫ"] = "EXPENSES", ["ПОТЕРИ"] = "LOSSES", ["Начать рейс"] = "Start trip",
        ["Завершить рейс"] = "Finish trip", ["Продолжить рейс"] = "Continue trip", ["Обновить из Game.log"] = "Refresh from Game.log",
        ["Автозаполнение из Game.log"] = "Autofill from Game.log", ["Торговые запросы из Game.log"] = "Trade requests from Game.log",
        ["Подставить"] = "Use", ["Период статистики"] = "Statistics period", ["Экспорт CSV"] = "Export CSV",
        ["Удалить из статистики"] = "Remove from statistics", ["Вложено: "] = "Invested: ", ["    Получено: "] = "    Received: ",
        ["    Расходы и потери: "] = "    Expenses and losses: ", ["МОНИТОР ИГРЫ"] = "GAME MONITOR",
        ["Обновить данные"] = "Refresh data", ["Выбрать Game.log"] = "Choose Game.log", ["Папка игры"] = "Game folder",
        ["Проверка компьютера и установки"] = "PC and installation check", ["Сохранить отчёт"] = "Save report",
        ["История игровых сессий"] = "Game session history", ["Подключение к Star Citizen"] = "Star Citizen connection",
        ["Установка игры"] = "Game installation", ["Выбрать папку игры"] = "Choose game folder", ["Найти автоматически"] = "Detect automatically",
        ["Мониторинг игры"] = "Game monitoring", ["Автоматически читать игровой журнал"] = "Read the game log automatically",
        ["Обновлять каждые, секунд"] = "Update every, seconds", ["Отображение"] = "Display",
        ["Автоматический мониторинг"] = "Automatic monitoring", ["Game.log отслеживается постоянно"] = "Game.log is monitored continuously",
        ["Nexus читает только новые строки журнала. Частота API и OCR регулируется автоматически, чтобы не создавать лишнюю нагрузку."] = "Nexus reads only new log lines. API and OCR intervals are adjusted automatically to avoid unnecessary load.",
        ["Подробности торговых маршрутов"] = "Trade route details", ["Обновление программы"] = "Application update",
        ["Игровой оверлей"] = "Game overlay", ["Показывать оверлей при запущенном Star Citizen"] = "Show the overlay while Star Citizen is running",
        ["Расширенный вид: миссия, следующая остановка и свежесть данных"] = "Expanded view: mission, next stop, and data freshness",
        ["Бинд показать / скрыть"] = "Show / hide shortcut", ["Прозрачность"] = "Opacity",
        ["Очистить"] = "Clear",
        ["Можно нажать любую свободную комбинацию клавиатуры. Escape отменяет запись, Delete очищает бинд."] = "Press any available keyboard shortcut. Escape cancels recording; Delete clears the shortcut.",
        ["Показать пример"] = "Show preview", ["Скрыть пример"] = "Hide preview",
        ["Компактный"] = "Compact", ["Расширенный"] = "Expanded",
        ["Оверлей — отдельное окно только для чтения. Можно назначить любое свободное сочетание; по умолчанию бинда нет. Nexus не внедряется в Star Citizen и не перехватывает управление."] = "The overlay is a separate read-only window. You can assign any available shortcut; none is assigned by default. Nexus does not inject into Star Citizen or intercept input.",
        ["Крестик сворачивает SC NEXUS в трей. Для полного выхода используй меню значка в области уведомлений."] = "The close button minimizes SC NEXUS to the notification area. Use the tray icon menu to exit completely.",
        ["Проверить обновление"] = "Check for updates", ["Токен для закрытого репозитория"] = "Token for a private repository",
        ["Сохранить токен"] = "Save token", ["Удалить токен"] = "Remove token", ["Личные данные"] = "Personal data",
        ["Резервная копия"] = "Backup", ["Открыть папку данных"] = "Open data folder", ["АВТОМАТИЧЕСКИЕ ДАННЫЕ"] = "AUTOMATIC DATA",
        ["Источники данных"] = "Data sources", ["Обнаружено в игре"] = "Detected in game", ["Обновить все источники"] = "Refresh all sources",
        ["OCR экрана"] = "Screen OCR", ["Использовать OCR, когда данных нет в журнале и API"] = "Use OCR when logs and APIs do not contain the value",
        ["Язык"] = "Language", ["Ручной выбор нужен только если установка не найдена автоматически."] = "Manual selection is shown only when automatic detection fails.",
        ["Данные и автоматизация"] = "Data and automation", ["Текущий корабль"] = "Current ship", ["Локация"] = "Location",
        ["Система"] = "System", ["Контур игры"] = "Game environment", ["Версия игры"] = "Game build",
        ["Текущая конфигурация"] = "Current loadout", ["Рыночные цены"] = "Market prices", ["Миссии"] = "Missions",
        ["Смерти"] = "Deaths", ["Перемещения"] = "Movements", ["Найденные корабли"] = "Detected ships",
        ["Бой"] = "Combat", ["Путешествие и торговля"] = "Travel and trade", ["Подобрать конфигурации"] = "Build loadouts",
        ["Подставить доступный баланс"] = "Use available balance", ["Скопировать список деталей"] = "Copy component list",
        ["КОРАБЛЬ ИЗ ФЛОТА"] = "SHIP FROM FLEET", ["БЮДЖЕТ, aUEC"] = "BUDGET, aUEC", ["ПРИОРИТЕТ"] = "PRIORITY",
        ["ЛУЧШЕЕ ЗА БЮДЖЕТ"] = "BEST WITHIN BUDGET", ["ЛУЧШЕЕ БЕЗ ОГРАНИЧЕНИЯ БЮДЖЕТА"] = "BEST WITHOUT BUDGET LIMIT",
        ["ЭНЕРГИЯ, ОХЛАЖДЕНИЕ И КВАНТ"] = "POWER, COOLING, AND QUANTUM",
        ["Маршрут покупки компонентов"] = "Component shopping route", ["Остановки и список покупок"] = "Stops and shopping list",
        ["ВЕДЕНИЕ ПО МАРШРУТУ"] = "ROUTE GUIDANCE", ["Назад"] = "Back",
        ["Следующая остановка"] = "Next stop", ["Завершить ведение"] = "Stop guidance",
        ["Остановка переключается автоматически по Game.log. Если журнал не сообщил локацию или сделку, скорректируй шаг вручную."] = "The stop advances automatically from Game.log. If the log does not report the location or transaction, adjust the step manually.",
        ["Час"] = "Hour", ["День"] = "Day", ["Три дня"] = "Three days", ["Неделя"] = "Week",
        ["Месяц"] = "Month", ["Полгода"] = "Six months", ["Год"] = "Year", ["Прямой рейс"] = "Direct route",
        ["Цепочка"] = "Chain", ["Сбор груза"] = "Cargo collection", ["Все системы"] = "All systems",
        ["По названию"] = "By name", ["По вместимости"] = "By capacity", ["За рейс"] = "Per trip",
        ["За час"] = "Per hour",
        ["Все маршруты"] = "All routes", ["Не выбран"] = "Not selected", ["Не указана"] = "Not specified"
        , ["Корабль, бюджет и подходящие торговые рейсы"] = "Ship, budget, and matching trade routes"
        , ["Твои корабли и каталог моделей"] = "Your ships and the vehicle catalog"
        , ["Подбор оснащения под бюджет и задачу"] = "Loadout recommendations for your budget and role"
        , ["Текущий рейс, фактические суммы и история"] = "Current trip, actual amounts, and history"
        , ["Состояние игры, проверка файлов и журнал сессий"] = "Game status, file checks, and session history"
        , ["Подключение к игре, отображение и сохранение данных"] = "Game connection, display, and saved data"
        , ["Всё для следующего вылета"] = "Everything for your next flight"
        , ["Подбор сменных деталей по совместимым портам выбранного корабля. Сравниваются измеримые характеристики из текущего каталога игры."] = "Recommends replaceable components for compatible ports on the selected ship using measurable stats from the current game catalog."
        , ["Ракеты и другие системы без проверенной совместимости в расчёт не входят. Орудия турелей требуют экипажа. Общая нагрузка на питание и охлаждение пока не проверяется: перед покупкой проверь сборку в игре."] = "Missiles and systems without verified compatibility are excluded. Turret guns may require crew. Total power and cooling load is not validated, so verify the build in game before buying."
        , ["OCR экрана работает только когда окно Star Citizen находится на переднем плане. Снимок удаляется сразу после распознавания."] = "Screen OCR runs only while Star Citizen is the foreground window. The screenshot is deleted immediately after recognition."
        , ["Готов к следующему рейсу?"] = "Ready for the next trip?"
        , ["Текущая локация"] = "Current location", ["Сервер и регион"] = "Server and region"
        , ["Активная миссия"] = "Active mission"
        , ["Заработано сегодня"] = "Earned today", ["За завершённые рейсы"] = "From completed trips"
        , ["Средняя прибыль в час"] = "Average profit per hour", ["ЗАРАБОТАНО"] = "EARNED"
        , ["МАРШРУТ И ФАКТИЧЕСКИЕ СУММЫ"] = "ROUTE AND ACTUAL AMOUNTS", ["ТИП МАРШРУТА"] = "ROUTE TYPE"
        , ["Статистика по завершённым рейсам"] = "Completed trip statistics", ["Рейс в процессе"] = "Trip in progress"
        , ["Открыть флот"] = "Open fleet", ["Роль определяется по модели"] = "Role is inferred from the model"
        , ["Введи часть названия корабля"] = "Enter part of the ship name", ["Введи минимум две буквы и выбери стартовую точку"] = "Enter at least two letters and select a starting point"
        , ["Например: Stanton или Tressler"] = "For example: Stanton or Tressler"
        , ["Выбери корабль и нажми «Подобрать конфигурации»."] = "Select a ship and click “Build loadouts”."
        , ["Выбери корабль из своего флота для этого рейса. При завершении прибыль добавится к балансу."] = "Select a ship from your fleet. Profit will be added to the balance when the trip is completed."
        , ["Выбери модель из каталога. Вместимость и роль заполнятся автоматически."] = "Select a model from the catalog. Capacity and role will be filled automatically."
        , ["Журнал показывает запросы к терминалу. Подставь сумму в рейс только после проверки сделки в игре."] = "The log shows terminal requests. Apply an amount only after verifying the transaction in game."
        , ["Запас и спрос меняются в игре. Перед закупкой проверь терминал; прибыль рассчитана по последним котировкам UEX."] = "Stock and demand change in game. Check the terminal before buying; profit uses the latest UEX quotes."
        , ["Из журнала берутся сервер, регион, сессии и торговые запросы. Ручная проверка доступна в инструментах."] = "The log provides server, region, sessions, and trade requests. Manual refresh is available in Tools."
        , ["Используются стартовая точка и фильтры выше. Для цепочки минимум заполнения применяется к каждому участку. Подбор предлагает выгодные варианты, но не гарантирует кратчайший или самый прибыльный путь."] = "The start point and filters above are used. Minimum fill applies to each chain leg. Suggested routes are profitable candidates, but may not be the shortest or most profitable possible path."
        , ["История ниже отфильтрована по времени завершения. Активный рейс всегда виден."] = "History is filtered by completion time. The active trip is always visible."
        , ["Показывать цены за SCU, запас и спрос в каждой карточке."] = "Show per-SCU prices, stock, and demand on every card."
        , ["Резервная копия содержит всю базу. Для переноса закрой приложение и замени nexus.db в папке данных сохранённой копией."] = "A backup contains the entire database. To transfer it, close the app and replace nexus.db in the data folder with the saved copy."
        , ["Флот, рейсы и настройки сохраняются автоматически на этом компьютере."] = "Fleet, trips, and settings are saved automatically on this computer."
        , ["Суммы из журнала подставляются автоматически при включённом мониторинге. Маршрут и все поля остаются редактируемыми до завершения рейса."] = "Log amounts are applied automatically while monitoring is enabled. The route and all fields remain editable until the trip is completed."
        , ["Для открытого репозитория токен не нужен. Если доступ к нему будет закрыт, добавь токен GitHub с правом Contents: Read."] = "A public repository does not require a token. If it becomes private, add a GitHub token with Contents: Read access."
        , ["Добавь первый корабль"] = "Add your first ship", ["Выбран:"] = "Selected:", ["Активный корабль:"] = "Active ship:"
        , ["Вместимость выбранного корабля:"] = "Selected ship capacity:", ["Вместимость:"] = "Capacity:"
        , ["Последний сервер:"] = "Last server:", ["Журнал обновлён:"] = "Log updated:"
        , ["Доступно на закупку с учётом резерва:"] = "Available for purchases after reserve:"
        , ["Старт:"] = "Start:", ["Закупка:"] = "Purchase:", ["Покупка:"] = "Buy:", ["Продажа:"] = "Sell:"
        , ["⚠ ОПАСНО: Pyro"] = "⚠ DANGER: Pyro", ["Данные:"] = "Data:", ["цены магазинов:"] = "store prices:"
        , ["Получено:"] = "Received:", ["Расходы и потери:"] = "Expenses and losses:"
        , ["Добавь корабль во флот, чтобы подобрать оснащение."] = "Add a ship to your fleet to build a loadout."
        , ["Добавь корабль во флот и выбери его здесь."] = "Add a ship to your fleet and select it here."
        , ["Укажи бюджет и нажми «Подобрать конфигурации»."] = "Enter a budget and click “Build loadouts”."
        , ["Укажи бюджет в aUEC: целое число не меньше нуля."] = "Enter a non-negative whole-number budget in aUEC."
        , ["Загружаю порты корабля, детали и цены…"] = "Loading ship ports, components, and prices…"
        , ["Подбор завершён."] = "Loadout calculation complete.", ["Список деталей скопирован."] = "Component list copied."
        , ["За SCU"] = "Per SCU", ["Маржа"] = "Margin", ["Вход"] = "Investment", ["Заполнение"] = "Fill"
        , ["Местный"] = "Local", ["Планетарный"] = "Planetary", ["Звёздный"] = "In-system", ["Межзвёздный"] = "Interstellar"
        , ["Бомбардировщик"] = "Bomber", ["Медицинский"] = "Medical", ["Добыча ресурсов"] = "Mining"
        , ["Утилизация"] = "Salvage", ["Заправка"] = "Refueling", ["Поддержка и ремонт"] = "Support and repair"
        , ["Грузоперевозки"] = "Cargo", ["Военный транспорт"] = "Military transport", ["Исследование"] = "Exploration"
        , ["Пассажирский"] = "Passenger", ["Гоночный"] = "Racing", ["Передача данных"] = "Data running"
        , ["Боевое"] = "Combat", ["Универсальный"] = "General purpose"
        , ["Ежедневные резервные копии базы создаются автоматически и хранятся семь дней."] = "Daily database backups are created automatically and kept for seven days."
        , ["Операционная система"] = "Operating system", ["Процессор"] = "Processor"
        , ["логических процессоров:"] = "logical processors:", ["Логических процессоров:"] = "Logical processors:"
        , ["Оперативная память"] = "Memory", ["Всего "] = "Total ", [" ГБ · доступно "] = " GB · available "
        , [" ГБ · занято "] = " GB · used ", ["Файл подкачки"] = "Page file"
        , ["Настроен:"] = "Configured:", ["Не найден в настройках Windows. Отключённый файл подкачки может вызывать вылеты при нехватке RAM."] = "Not configured in Windows. A disabled page file may cause crashes when RAM runs out."
        , ["Не удалось прочитать настройку Windows."] = "Could not read the Windows setting."
        , ["Установка игры"] = "Game installation", ["Не найдена. Выбери папку LIVE, PTU или EPTU в настройках."] = "Not found. Select the LIVE, PTU, or EPTU folder in Settings."
        , ["Исполняемый файл"] = "Executable", [" найден · версия "] = " found · version "
        , ["не указана"] = "not reported", [" не найден. Проверь выбранную папку игры."] = " was not found. Check the selected game folder."
        , ["Игровой журнал"] = "Game log", ["Найден · обновлён "] = "Found · updated "
        , ["Game.log пока отсутствует."] = "Game.log does not exist yet.", ["Место на диске"] = "Disk space"
        , ["Свободно "] = "Free ", [" ГБ на "] = " GB on ", [" · рекомендуется освободить место перед обновлением игры"] = " · free some space before updating the game"
        , ["Личный конфиг"] = "Custom config", ["user.cfg найден"] = "user.cfg found"
        , ["user.cfg не найден; игра может использовать настройки по умолчанию"] = "user.cfg not found; the game may be using default settings"
        , ["Файлы локализации"] = "Localization files", ["global.ini не найден"] = "global.ini not found"
        , ["Найдены папки:"] = "Found folders:", ["Локальные кэши Star Citizen"] = "Local Star Citizen caches"
        , ["Папок:"] = "Folders:", [" · размер около "] = " · approximately ", [" ГБ. Nexus ничего не удаляет автоматически."] = " GB. Nexus never deletes these files automatically."
        , ["Папка лаунчера найдена; журналы отсутствуют."] = "Launcher folder found; no logs are present."
        , ["Последний журнал:"] = "Latest log:", ["Последние ошибки игры"] = "Recent game errors"
        , ["В последних 2 МБ Game.log известные критические сигнатуры не найдены."] = "No known critical signatures were found in the last 2 MB of Game.log."
        , ["Обнаружен C0000005 / access violation. Причину нужно уточнять по контексту журнала и драйверам."] = "C0000005 / access violation detected. Check the surrounding log entries and drivers."
        , ["Зафиксирован сброс графического драйвера или устройства GPU."] = "A graphics driver or GPU device reset was recorded."
        , ["В журнале есть признак сбоя GPU."] = "The log contains a GPU crash signature."
        , ["Игра сообщила о нехватке системной памяти."] = "The game reported insufficient system memory."
        , ["В журнале встречается watchdog: возможное зависание игрового потока."] = "A watchdog entry may indicate a stalled game thread."
        , ["Обнаружена сетевая ошибка 30000."] = "Network error 30000 detected."
        , ["Обнаружена ошибка соединения 30007."] = "Connection error 30007 detected."
        , ["Обнаружена ошибка входа 19000."] = "Login error 19000 detected."
        , ["Графический адаптер"] = "Graphics adapter", [" · драйвер "] = " · driver "
        , ["Журнал ошибок SC NEXUS"] = "SC NEXUS error log", ["Записи найдены:"] = "Entries found:"
        , ["Необработанные ошибки не зарегистрированы."] = "No unhandled errors have been recorded."
        , ["маржа"] = "margin", ["Запас "] = "Stock ", [" · спрос "] = " · demand "
        , ["Котировка:"] = "Quote:", ["По твоей истории:"] = "From your history:"
        , ["Время появится после первого завершённого рейса по этому маршруту"] = "Travel time will appear after the first completed trip on this route"
        , ["Обычно у тебя:"] = "Your usual time:", ["Твоего времени пока нет"] = "No personal travel time yet"
        , ["заполнено"] = "filled", ["Риск не оценён"] = "Risk not assessed"
        , ["Автоподбор конечной точки"] = "Automatic destination", ["остановок:"] = "stops:"
        , ["Трюм:"] = "Cargo hold:", [" · свободно "] = " · free ", ["Доступно после остановки:"] = "Available after stop:"
        , ["Макс. загрузка"] = "Peak cargo", [" · закупки всего "] = " · total purchases "
        , ["Самая старая котировка:"] = "Oldest quote:", ["Ожидаемая прибыль:"] = "Expected profit:"
        , ["Проверь цены и наличие в игре. Время, топливо и стоимость перелётов не включены."] = "Verify prices and availability in game. Travel time, fuel, and flight costs are not included."
        , ["Запрос покупки"] = "Purchase request", ["Запрос продажи"] = "Sale request"
        , ["В пути"] = "In progress", ["Уже установлен"] = "Already installed", ["Цена неизвестна"] = "Price unknown"
        , ["Магазин не указан"] = "Store not specified", ["Неизвестная система"] = "Unknown system", ["Цена от "] = "Price from "
        , ["Характеристики не загружены"] = "Stats not loaded", ["Характеристики не указаны"] = "Stats not available"
        , ["Прочность "] = "Strength ", [" · восстановление "] = " · regeneration "
        , ["Скорость "] = "Speed ", [" · расход неизвестен"] = " · consumption unknown", [" · расход "] = " · consumption "
        , ["Энергия "] = "Power ", ["Охлаждение "] = "Cooling ", [" · прочность "] = " · health "
        , ["Устойчивый DPS (60 с):"] = "Sustained DPS (60 s):", [" · пиковый DPS:"] = " · burst DPS:"
        , ["Стоимость замены:"] = "Replacement cost:", ["Известная стоимость:"] = "Known cost:"
        , [" · без цены:"] = " · without price:", ["Подтверждённые слоты компонентов рассчитаны"] = "Verified component slots calculated"
        , ["Не удалось подобрать для "] = "No compatible component for ", [" слотов"] = " slots"
        , ["Заменить компонентов:"] = "Components to replace:", [" · оставить штатными:"] = " · keep installed:"
        , ["Купить "] = "Buy ", ["Продать "] = "Sell ", [" SCU за "] = " SCU for "
        , ["На этой остановке:"] = "At this stop:", [" за шт."] = " each", ["Энергия и охлаждение: данных недостаточно"] = "Power and cooling: insufficient data"
        , ["Энергия:"] = "Power:", ["Охлаждение:"] = "Cooling:", [" сегм. · резерв "] = " segments · reserve "
        , ["⚠ Расчётная нагрузка превышает выработку энергии."] = "⚠ Estimated load exceeds power generation."
        , ["⚠ Расчётная нагрузка превышает возможности охлаждения."] = "⚠ Estimated load exceeds cooling capacity."
        , ["Нагрузка рассчитана для сравниваемых сменных слотов; неподдерживаемые системы корабля не включены."] = "Load is calculated for the compared replaceable slots; unsupported ship systems are excluded."
        , ["Квантовый привод: данных недостаточно"] = "Quantum drive: insufficient data", ["Квант:"] = "Quantum:"
        , ["скорость неизвестна"] = "speed unknown", [" · расчётная дальность "] = " · estimated range "
        , ["Маршрут покупок не нужен или магазины не указаны."] = "No shopping route is needed or store data is unavailable."
        , ["Рекомендуемый маршрут:"] = "Recommended route:", ["остановка"] = "stop", ["остановки"] = "stops", ["остановок"] = "stops"
        , [" · покупки "] = " · purchases ", [" к минимальной сумме"] = " above the minimum total", [" · без переплаты"] = " · no extra cost"
        , ["Сначала сокращаем число остановок. Учитываются магазины, где каждая деталь дороже своей минимальной цены не более чем на 5%. Безопасные точки, текущие локация и система в приоритете, Pyro — в конце."] = "The planner first reduces the number of stops. It considers stores where each component costs no more than 5% above its lowest price. Safe locations, the current location, and the current system are preferred; Pyro is placed last."
        , ["Остановок:"] = "Stops:", [" · сумма:"] = " · total:"
        , ["Ракеты и системы без проверенной совместимости в расчёт не входят. Энергия и охлаждение считаются только для подтверждённых сменных слотов, поэтому итоговую сборку проверь в игре."] = "Missiles and systems without verified compatibility are excluded. Power and cooling are calculated only for verified replaceable slots, so verify the final build in game."
    };

    public static bool IsEnglish { get; private set; }

    public static void SetLanguage(string language)
    {
        IsEnglish = language.Equals("en", StringComparison.OrdinalIgnoreCase);
        var culture = CultureInfo.GetCultureInfo(IsEnglish ? "en-US" : "ru-RU");
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }

    public static string T(string value)
    {
        if (!IsEnglish || string.IsNullOrWhiteSpace(value)) return value;
        if (English.TryGetValue(value, out var exact)) return exact;
        var result = value;
        foreach (var pair in English.OrderByDescending(x => x.Key.Length))
            if (result.Contains(pair.Key, StringComparison.Ordinal)) result = result.Replace(pair.Key, pair.Value, StringComparison.Ordinal);
        return result;
    }
}

public static class UiLocalization
{
    private static readonly ConditionalWeakTable<DependencyObject, Original> Originals = new();

    public static void Apply(DependencyObject root)
    {
        Visit(root);
    }

    private static void Visit(DependencyObject item)
    {
        switch (item)
        {
            case TextBlock text:
                Translate(item, text.Text, BindingOperations.IsDataBound(text, TextBlock.TextProperty),
                    x => text.SetCurrentValue(TextBlock.TextProperty, x),
                    () => text.GetBindingExpression(TextBlock.TextProperty)?.UpdateTarget());
                break;
            case ContentControl content when content.Content is string value:
                Translate(item, value, BindingOperations.IsDataBound(content, ContentControl.ContentProperty),
                    x => content.SetCurrentValue(ContentControl.ContentProperty, x),
                    () => content.GetBindingExpression(ContentControl.ContentProperty)?.UpdateTarget());
                break;
            case HeaderedContentControl header when header.Header is string value:
                Translate(item, value, BindingOperations.IsDataBound(header, HeaderedContentControl.HeaderProperty),
                    x => header.SetCurrentValue(HeaderedContentControl.HeaderProperty, x),
                    () => header.GetBindingExpression(HeaderedContentControl.HeaderProperty)?.UpdateTarget());
                break;
        }
        var count = item is Visual or Visual3D ? VisualTreeHelper.GetChildrenCount(item) : 0;
        for (var i = 0; i < count; i++) Visit(VisualTreeHelper.GetChild(item, i));
        if (count == 0 && item is FrameworkElement element)
        {
            foreach (var child in LogicalTreeHelper.GetChildren(element).OfType<DependencyObject>()) Visit(child);
        }
    }

    private static void Translate(DependencyObject owner, string current, bool isBound, Action<string> set,
        Action restoreBinding)
    {
        if (LocalizationService.IsEnglish)
        {
            var translated = LocalizationService.T(current);
            if (translated == current) return;
            if (!isBound) Originals.GetValue(owner, _ => new Original(current));
            set(translated);
        }
        else if (isBound) restoreBinding();
        else if (Originals.TryGetValue(owner, out var original)) set(original.Value);
    }

    private sealed record Original(string Value);
}
