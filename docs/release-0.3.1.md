# SC NEXUS 0.3.1

## English

This release focuses on speed, reliability, and a cleaner daily workflow.

- Reduced duplicate work: `Game.log` is tailed incrementally, backup log discovery is cached, UEX requests are coalesced, and cached JSON stays in memory.
- Added a lightweight Windows process provider that notices Star Citizen start and exit within a few seconds without opening or reading the game process.
- Prevented old logs and history from replacing newer corrections; stale values are clearly marked as last known.
- Added source data versions, last successful provider updates, bounded in-memory event history, and indexed SQLite history queries.
- Fixed session detection after the active `Game.log` is truncated for a new game launch.
- Merged fleet and loadout planning into the Ships page and removed duplicate navigation and data blocks.
- Added personal profit-per-hour estimates to matching trade routes and grouped loadout purchases by store and quantity.
- Expanded read-only diagnostics with CPU, GPU, VRAM, driver, page file, disk, launcher, cache, and known recent crash signatures.
- Added rotating local error logs and one automatic database backup per day, keeping the latest seven copies.
- OCR now processes screenshots entirely in memory.
- Extended English translations for diagnostics and dynamic route/loadout text.

The application remains an external read-only companion: no DLL injection, memory reading, process hooks, packet interception, or modification of Star Citizen files.

## Русский

Версия сосредоточена на скорости, надёжности и более чистом ежедневном интерфейсе.

- Убрана лишняя работа: `Game.log` читается только с места последней записи, поиск резервных журналов кэшируется, одинаковые запросы UEX объединяются, разобранный JSON хранится в памяти.
- Добавлен лёгкий provider списка процессов Windows: запуск и закрытие Star Citizen определяются за несколько секунд без открытия и чтения процесса игры.
- Старые журналы и история больше не заменяют свежие исправления; устаревшие значения явно помечаются как последние известные.
- Добавлены версия исходных данных, время последнего успешного обновления источников, ограничение истории в памяти и индексы SQLite.
- Исправлено определение новой игровой сессии после обнуления активного `Game.log`.
- Флот и конфигуратор объединены на экране «Корабли», дублирующие навигация и блоки удалены.
- В торговых маршрутах появилась личная оценка прибыли в час, а покупки для сборки сгруппированы по магазинам и количеству.
- Диагностика только для чтения дополнена CPU, GPU, VRAM, драйвером, файлом подкачки, диском, лаунчером, кэшами и известными свежими сигнатурами вылетов.
- Добавлены локальный циклический журнал ошибок и ежедневная резервная копия базы с хранением последних семи копий.
- OCR теперь обрабатывает снимок только в памяти.
- Расширен английский перевод диагностики и динамических текстов маршрутов и конфигуратора.

Приложение остаётся внешним READ ONLY помощником: без DLL injection, чтения памяти, hooks, перехвата пакетов и изменения файлов Star Citizen.
