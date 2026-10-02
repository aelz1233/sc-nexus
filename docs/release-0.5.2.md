# SC NEXUS 0.5.2

## English

This maintenance update improves reliability of local data and makes Settings easier to use.

- Settings are now arranged as a compact vertical sequence of cards, so the page no longer leaves a large empty column.
- SQLite waits longer for an active local write instead of immediately failing when another application task is saving.
- Corrupted local databases are restored automatically from the newest verified backup at startup. The original file is retained as `nexus-corrupt-…db` in the data folder.
- Older saved settings with missing text values are repaired in memory before saving, preventing the `CurrentSystem` database error.
- Technical Entity Framework save errors are replaced with a clear, actionable status message; diagnostic details remain in the local log.
- Added regression coverage for backup restoration and legacy empty settings. All 80 automated tests pass.

## Русский

Техническое обновление надёжности локальных данных и настроек.

- Настройки выстроены в плотную вертикальную последовательность карточек: пустая колонка больше не остаётся.
- SQLite дольше ждёт активную локальную запись, вместо мгновенного сбоя при параллельном сохранении.
- При повреждении локальная база при запуске автоматически восстанавливается из самой свежей проверенной резервной копии. Исходный файл сохраняется в папке данных как `nexus-corrupt-…db`.
- Старые настройки с пустыми текстовыми полями исправляются в памяти до сохранения: устранён сбой `CurrentSystem`.
- Техническая ошибка Entity Framework заменена понятным статусом с действием; детали остаются в локальном журнале.
- Добавлены регрессионные тесты восстановления из резервной копии и старых пустых настроек. Проходят все 80 автоматических тестов.
