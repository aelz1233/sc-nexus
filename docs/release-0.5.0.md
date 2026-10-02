# SC NEXUS 0.5.0

## English

This release improves long-running reliability, turns the overlay into a configurable companion, and makes component upgrades easier to compare and buy.

- Added an overlay editor with drag positioning, five anchor modes, 75–150% scaling, separate background/text opacity, and per-block visibility.
- Added a dashboard session report and notification center. Notifications cover game sessions, detected ships, data providers, route progress, and updates.
- Expanded the tray menu with current game/location/route status and controls for the overlay, monitoring, and updates.
- Added current-versus-recommended loadout comparison and three component shopping strategies: balanced, fewer flights, and lowest price. Routes respect Pyro and NQA filters and include shopping checklists.
- Pausing monitoring now stops provider polling. Individual provider timeouts prevent one failed source from blocking all automatic data.
- Debounced ship/location search, pruned stale log cursors and parser state, capped retained observation history, and added SQLite WAL/index tuning.
- Updates now create a verified pre-update database backup and preserve the previous executable. A failed startup schedules an automatic rollback.
- Extended Russian and English interface text for the new controls.
- Added regression tests for shopping strategies, safety filters, monitoring pause, update rollback state, and pre-update backups. All 77 automated tests pass.

## Русский

В этом выпуске повышена надёжность долгой работы, оверлей получил полноценную настройку, а улучшения корабля стало проще сравнивать и покупать.

- Добавлен редактор оверлея: перетаскивание, пять вариантов положения, масштаб 75–150%, отдельная прозрачность фона и текста и отключение отдельных блоков.
- На главном экране появились отчёт по игровой сессии и центр уведомлений. Уведомления сообщают о сессиях, обнаруженных кораблях, источниках данных, ходе маршрута и обновлениях.
- В меню трея добавлены состояние игры, локация и маршрут, а также управление оверлеем, мониторингом и обновлением.
- Конфигуратор сравнивает текущую и рекомендуемую сборки и строит три маршрута закупки: сбалансированный, с минимумом перелётов и с минимальной ценой. Маршруты учитывают фильтры Pyro/NQA и содержат чек-листы.
- Приостановка мониторинга теперь прекращает опрос providers. Тайм-аут каждого источника не позволяет одному сбою остановить весь автоматический сбор.
- Добавлена задержка поиска кораблей и локаций, очищаются старые курсоры и состояние парсера, ограничена история наблюдений, настроены WAL и индексы SQLite.
- Перед обновлением создаётся проверенная копия базы и сохраняется предыдущий EXE. Ошибка запуска новой версии включает автоматический откат.
- Дополнен русский и английский текст новых элементов интерфейса.
- Добавлены регрессионные тесты стратегий закупки, фильтров безопасности, паузы мониторинга, состояния отката и резервной копии перед обновлением. Проходят все 77 автоматических тестов.
