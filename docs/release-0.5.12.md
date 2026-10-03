# SC NEXUS 0.5.12

## Русский

- Уведомления Game.log «Начислено … aUEC» рядом с завершением контракта сохраняются как выплаты. Повторы не увеличивают заработок. При нескольких одновременных завершениях выплата хранится без угадывания конкретного контракта.
- В настройках добавлен переключатель учёта контрактов в аналитике. История сохраняется при отключении.
- Выплаты входят в заработок за день, выбранный период, сессию и расчёт дохода в час. Пересекающееся время не считается дважды; при неизвестной длительности доход в час не выдумывается.
- В журнале можно подтвердить отсутствующую выплату и исключить запись из статистики. Записи не прибавляются к балансу повторно: баланс обновляется через OCR.
- Награда на экране предложения может быть подставлена для проверки. Просмотр предложения сам по себе не считается заработком.
- Форма торгового рейса свёрнута по умолчанию.
- Пройдены 142 теста. OCR предложения проверен на изображении с наградой 50 500 aUEC. Не все сборки игры записывают уведомления о выплатах.

## English

- Game.log payout notifications near contract completion are recorded as earnings. Replays do not increase totals. When several contracts complete together, payouts are kept without guessing a specific contract.
- Added a settings toggle for including contract payouts in analytics; disabling it preserves history.
- Payouts contribute to daily, selected-period and session earnings and hourly income. Overlapping activity time is counted once; unknown duration is shown explicitly.
- Journal supports confirming missing payouts and excluding entries. Analytics records never add money to the wallet again; OCR updates the balance.
- A visible offer reward can prefill the amount for verification. Viewing an offer is not earned income.
- The trading trip form is collapsed by default.
- All 142 tests passed. Offer OCR was checked against a 50,500 aUEC screenshot. Not every game build logs payout notifications.
