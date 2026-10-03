# SC NEXUS 0.5.5

## Русский

- Бинд оверлея дополнен проверкой назначенной клавиши при активной игре; исключены двойное срабатывание и конфликт с записью нового бинда. Скрытие работает и после режима размещения.
- Оверлей получил управление видом, кнопку скрытия, раздельные подписи источников, прогресс маршрута и отметку старых миссий из истории.
- Добавлены чек-листы рейса и закупки компонентов. План из конфигуратора открывается в оверлее, показывает остановки и оставшуюся сумму, сохраняет отметки после перезапуска. Можно вернуться к предыдущей остановке.
- UI-проверки изолированы от запущенного Nexus и пользовательской базы.

- Переключатель «Лучшее за бюджет / Максимальная конфигурация» получил единое тёмное оформление и чёткое выделение активного варианта.
- Убрана стандартная пунктирная рамка вокруг результата. При навигации клавиатурой фокус обозначается рамкой самого переключателя.
- Добавлен отступ между переключателем и стоимостью сборки. Выделение вкладки больше не делает весь результат полужирным.

## English

- Added a read-only check of the assigned shortcut while the game is active, deduplication of hotkey events, and safe shortcut capture. Hiding also exits positioning mode.
- Added overlay view/hide controls, separate source labels, route progress, and historical mission labels.
- Added trip and component purchase checklists. Send a configurator shopping plan to the overlay, follow store stops, review remaining cost, and keep progress across restarts. Previous stops can be revisited.
- Isolated UI tests from the running Nexus instance and user database.

- Restyled the budget/best-possible loadout selector with a unified dark background and a clear active state.
- Removed the default dotted outline around the result. Keyboard focus is indicated on the selector itself.
- Added spacing above the build cost. Selecting a tab no longer makes the entire result bold.
