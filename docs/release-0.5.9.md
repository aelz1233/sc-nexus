# SC NEXUS 0.5.9

## Русский

- Исправлен баланс: суммы переводов, цены и отдельные строки aUEC из Game.log больше не считаются остатком на счёте. Старые значения баланса не восстанавливаются из истории наблюдений.
- OCR распознаёт поле кошелька рядом с «Главная» в mobiGlas: область увеличивается для чтения мелкого шрифта. Для автоматического обновления суммы нужны два совпадающих свежих чтения. Неоднозначные суммы пропускаются; распознавание баланса не ждёт загрузки каталога кораблей.
- На обзоре и в оверлее показываются подтверждённые активные миссии текущей сессии. Записи из прошлых сессий остаются в журнале. Завершение отдельной цели больше не завершает всю миссию.
- Добавлены количество текущих миссий, цели и источник данных. Известные технические названия заменены понятными подписями, исходные имена доступны в подсказках. Полный ID сервера также перенесён в подсказку.
- Старые локации помечены как последние известные. Торговый бюджет показывает баланс Nexus и резерв отдельно.
- Проверены 133 тестов и распознавание пользовательского изображения баланса. Полный список контрактов и их официальные названия доступны не во всех журналах; отсутствие подтверждения не означает отсутствие миссий в игре.

## English

- Transfer amounts, prices and standalone aUEC log lines are no longer treated as wallet balances. Old balance observations are not replayed from history.
- OCR reads the wallet field near Home in mobiGlas using an enlarged region. Automatic balance updates require two matching recent readings; ambiguous amounts are skipped. Reading a balance no longer waits for the ship catalog.
- Dashboard and overlay show confirmed active missions from the current session. Earlier observations remain in Journal. Completing an objective no longer completes the entire contract.
- Added current mission count, objectives and source details. Known internal names use readable labels with original names in tooltips; the full shard ID is also available in a tooltip.
- Earlier locations are labeled as last known. Trading budget shows the Nexus balance and reserve separately.
- Verified 133 tests and OCR against a supplied balance screenshot. Logs may omit contracts or official titles; missing confirmation does not mean there are no missions in the game.

- Названия принятых контрактов берутся из уведомлений игры по ID миссии (русский/английский), независимо от языка Nexus.
- Accepted contract titles are matched by mission ID from Russian/English game notifications, independently of the Nexus language.
