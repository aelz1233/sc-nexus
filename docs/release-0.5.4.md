# SC NEXUS 0.5.4

## Русский

- Исправлено сохранение рейсов: завершение или удаление рейса и изменение баланса записываются одной транзакцией.
- Повторные наблюдения больше не сбрасывают ручные изменения баланса, локации и выбранного корабля. Обновление флота сохраняет введённые суммы активного рейса.
- Восстановление базы проверяет наличие данных Nexus, подготавливает копию перед заменой и сохраняет повреждённый оригинал. Ежедневная очистка больше не затрагивает копии перед обновлением.
- OCR возвращает результат текущей проверки, различает варианты моделей и не принимает цену товара за баланс. Для сканирования требуется активное окно игры; после нажатия кнопки есть три секунды на переключение. Большие изображения уменьшаются до допустимого размера Windows OCR.
- Оверлей пропускает клики вне кнопки обнаружения корабля; кнопка не забирает фокус у игры. При ручном сканировании оверлей временно скрывается. Вместо постоянной надписи LIVE показывается обнаруженный контур игры.
- Убраны ложные уведомления о восстановлении источников при запуске. Добавлены переводы состояний и возраст данных миссии, исправлена компоновка автозаполнения рейса. Словарь перевода сортируется один раз вместо повторной сортировки для каждого элемента.
- Повторный запуск открывает существующее окно, чтобы две копии приложения не записывали одну базу одновременно.

Проверка: 91 автоматический тест; отрисовка шести экранов на русском и английском при 1100×720 и 1440×900; жизненный цикл окон оверлея. Распознавание настоящего игрового экрана и прохождение кликов в работающей Star Citizen требуют отдельной проверки в игре. Отсутствие всех возможных ошибок не гарантируется.

## English

- Trip completion/deletion and balance adjustments now commit in one database transaction.
- Repeated observations no longer overwrite manual balance, location, or ship corrections. Fleet refresh preserves edited amounts for the active trip.
- Database recovery validates Nexus data, stages the replacement, and preserves the damaged original. Daily retention no longer removes pre-update backups.
- OCR reports the current scan, distinguishes model variants, and does not treat shop prices as wallet balance. The game must be in the foreground; manual scans allow three seconds to switch back. Large captures are resized to the Windows OCR limit.
- The overlay passes clicks through outside the detection button. The button does not take game focus. Manual capture temporarily hides the overlay. The header displays the detected game environment instead of a fixed LIVE label.
- Removed false source-recovery notifications at startup, added missing translations and mission-data age, and corrected trip autofill layout. Translation replacements are sorted once rather than for every UI element.
- Launching another copy restores the existing window to avoid simultaneous database writers.

Validation: 91 automated tests; six screens rendered in Russian and English at 1100×720 and 1440×900; overlay window lifecycle. Actual game-screen recognition and click-through interaction with a running Star Citizen instance still need an in-game check. This audit cannot guarantee the absence of every possible bug.
