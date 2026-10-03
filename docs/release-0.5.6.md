# SC NEXUS 0.5.6

## Русский

- «Принудительно обнаружить корабль» умеет читать список ASOP с прокруткой вверх и вниз. Esc, уход из окна игры или исчезновение терминала останавливают проверку. Кнопки вызова, восстановления и покупки не нажимаются.
- Заблокированные строки и строки с нечитаемым статусом пропускаются. Восстановление и доставка учитываются; одинаковые модели добавляются один раз. Доступная копия учитывается даже при наличии заблокированной копии той же модели.
- Добавлено совместное распознавание английских названий и русских статусов при наличии соответствующих языков Windows OCR.
- Категории кораблей используют признаки UEX. Повторное обнаружение обновляет категорию и вместимость, сохраняя заметки и историю.
- Добавлены подсказка в оверлее и инструкции на русском и английском. Сканирование ограничено 60 шагами / 100 секундами; нечитаемые строки, фильтры ASOP или прерванная проверка могут дать неполный список. Существующий флот автоматически не удаляется.

## English

- Force ship detection can scan the ASOP list by scrolling to the top and then down. Esc, leaving the game window or losing the terminal stops scanning. Retrieve, claim and purchase buttons are never pressed.
- Locked entries and unreadable statuses are skipped. Claim/delivery entries are included; models are deduplicated, retaining an unlocked copy even if another copy is locked.
- English ship names and Russian statuses are recognized together when the corresponding Windows OCR languages are installed.
- Ship categories use UEX metadata. Repeated detection updates role and cargo capacity while preserving notes and history.
- Added an overlay hint and bilingual instructions. Scans are limited to 60 steps / 100 seconds and can be incomplete because of unreadable rows, ASOP filters or interruption. Existing fleet entries are not deleted automatically.
