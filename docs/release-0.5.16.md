# SC NEXUS 0.5.16

## English

- Added light and dark themes, rounded controls and grouped settings.
- Replaced route cards with a searchable, sortable table, commodity illustrations and a selected-route inspector. Additional filters stay collapsed; Pyro/NQA warnings include text and an icon.
- Contract reward lookup uses the definition ID found in Game.log. Catalog amounts are estimates for the stated game version, not confirmed payouts. Some contracts have no published monetary reward.
- Contract earnings can be excluded from analytics in Settings → General or Journal. Saved records remain; wallet balance is not changed.
- Mission acceptance time is retained for duration calculation when completion is observed.
- Automatic ASOP scans now use the detailed OCR pass. Wallet parsing rejects unrelated alphanumeric labels; ambiguous fuzzy ship matches are rejected.
- Includes the balance/ASOP OCR and public updater fixes from 0.5.15.
- Updated the English interface screenshots and both README files. Screenshots use illustrative data.

OCR recognition depends on the visible game UI and supported data; this release does not guarantee detection of every balance, ship or contract reward. Regression tests cover known inputs; a new live-game OCR check was not performed for this release.

## Русский

- Добавлены светлая и тёмная темы, закругления и группы настроек.
- Маршруты представлены таблицей с поиском, сортировкой, иллюстрациями товаров и подробностями выбранного рейса. Редкие фильтры свёрнуты, опасность Pyro/NQA отмечена значком и текстом.
- Поиск награды использует ID типа контракта из Game.log. Сумма из каталога относится к указанной версии игры и не подтверждает выплату. Для некоторых заданий сумма отсутствует.
- Учёт контрактов можно выключить в настройках или журнале. Записи сохраняются, баланс не меняется.
- Время принятия сохраняется для расчёта длительности при получении события завершения.
- Детальный проход ASOP теперь используется и при автоматическом сканировании. Посторонний текст с цифрами не принимается за баланс, неоднозначные совпадения кораблей отклоняются.
- Включены исправления OCR баланса/ASOP и обновлятора из 0.5.15.
- Обновлены README на двух языках и английские снимки интерфейса с демонстрационными данными.

OCR зависит от экрана игры и доступных данных; распознавание каждого баланса, корабля или награды не гарантируется. Известные случаи покрыты тестами; новая проверка OCR в игре для этого выпуска не проводилась.
