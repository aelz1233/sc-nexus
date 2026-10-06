# SC NEXUS 0.5.13

## Русский

- Исправлена загрузка конфигураций для Ares Inferno Starfighter и других кораблей, названия которых отличаются между UEX и Star Citizen Wiki.
- Улучшено сопоставление названий кораблей между разными источниками данных.
- Добавлены повторные запросы при временных ошибках API и SSL-соединения.
- Увеличен таймаут запросов к Star Citizen Wiki.
- Загрузка компонентов сделана стабильнее: убраны проблемные параллельные запросы.
- Улучшена пагинация каталога компонентов.
- Добавлен резервный подбор цен при различиях версий данных.
- Пройдены 142 теста.

## English

- Fixed loadout loading for Ares Inferno Starfighter and other ships with different names between UEX and Star Citizen Wiki.
- Improved ship name matching between data sources.
- Added retries for temporary API and SSL connection failures.
- Increased Star Citizen Wiki request timeout.
- Improved component loading stability by avoiding problematic parallel requests.
- Improved component catalog pagination.
- Added price fallback when game data versions differ.
- All 142 tests passed.