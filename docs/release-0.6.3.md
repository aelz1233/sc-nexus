# SC NEXUS 0.6.3

## English
- Refined the dashboard, route planner, and settings layout in both themes. Route selection is clearer, and the inspector adapts to narrower windows while short result lists remain compact.
- Commodity names remain in English in both interface languages.
- Route and voyage planning refreshes stale market data and warns when only old quotes are available. Unavailable stock and demand no longer produce routes.
- Manual balance corrections survive a restart without being overwritten by older game-log readings. Automatic database backups continue on the next day even if the app stays open.
- Updates now follow the latest published GitHub release and use the saved token when needed. Downloaded market and component data remains usable if the disk cache cannot be written.
- Improved ship portrait matching and added regression checks for data, updates, and the Windows interface.

## Русский
- Доработаны компоновка обзора, маршрутов и настроек в обеих темах. Выбранный рейс заметнее, панель деталей подстраивается под узкое окно, а короткие списки остаются компактными.
- Названия товаров остаются английскими при любом языке интерфейса.
- Перед расчётом маршрутов и планов обновляются устаревшие котировки; при использовании старых данных показывается предупреждение. Маршруты без доступного запаса или спроса исключены.
- Ручная корректировка баланса сохраняется после перезапуска и не заменяется более старой записью журнала. Ежедневное резервное копирование продолжается, даже если приложение остаётся открытым несколько дней.
- Обновления берутся из последнего опубликованного релиза GitHub и при необходимости используют сохранённый токен. Загруженные данные рынка и компонентов доступны даже при ошибке записи кеша.
- Улучшен подбор изображений кораблей; добавлены проверки данных, обновлений и интерфейса Windows.
