## English

SC NEXUS 0.3.0 introduces an automatic Data Collection Layer designed around minimal manual input.

- Incremental real-time monitoring of `Game.log` and `logbackups` without rereading the full file.
- Read-only local game detection for LIVE, PTU, EPTU, build information, and sessions.
- Provider architecture for Game.log, local files, UEX, Star Citizen Wiki, OCR, Nexus history, and manual fallback.
- Every automatic value includes its source, timestamp, and confidence.
- Structured mission/objective history, trade events, locations, balance events, movements, detected ships, and component catalog metadata are persisted locally when available.
- Optional foreground-window OCR with immediate screenshot deletion.
- Russian/English application UI and bilingual installer.
- Manual game folder selection is hidden when automatic detection succeeds.

The application remains an external read-only companion and does not use injection, process memory access, hooks, or packet interception.

## Русский

SC NEXUS 0.3.0 добавляет единый слой автоматического сбора данных с минимальным ручным вводом.

- Инкрементальное отслеживание `Game.log` и `logbackups` без повторного чтения всего файла.
- Автопоиск LIVE, PTU и EPTU, build и игровых сессий только в режиме чтения.
- Provider-архитектура для Game.log, локальных файлов, UEX, Star Citizen Wiki, OCR, истории Nexus и ручного fallback.
- У каждого автоматического значения есть источник, время и уверенность.
- При наличии надёжных данных локально сохраняются миссии/objectives, торговые события, локации, баланс, перемещения, обнаруженные корабли и сведения о каталоге компонентов.
- Необязательный OCR окна игры с немедленным удалением снимка.
- Русский и английский интерфейс приложения и установщика.
- Ручной выбор папки скрыт, если игра найдена автоматически.

Приложение остаётся внешним READ ONLY помощником и не использует injection, чтение памяти процесса, hooks или перехват пакетов.
