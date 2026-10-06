# SC NEXUS 0.5.15

## Русский

- Улучшено распознавание баланса в mobiGlas: допускаются типичные ошибки OCR вроде O/0, I/1 и неточное чтение подписи «Главная».
- Баланс выбирается по положению рядом с кошельком, а посторонние суммы дальше от него больше не мешают распознаванию.
- Улучшено распознавание ASOP: добавлен детальный OCR-проход по терминалу.
- Названия кораблей теперь сопоставляются с каталогом с допуском небольших OCR-ошибок вместо только точного совпадения.
- Расширено распознавание заголовков ASOP и статусов Stored/Claim/Delivery/Retrieve/Available и русских вариантов.
- Добавлены регрессионные тесты для баланса и кораблей.

## English

- Improved mobiGlas balance OCR with tolerance for common O/0, I/1 and Home-label recognition errors.
- Wallet amount selection now uses screen geometry so unrelated nearby amounts are less likely to block detection.
- Improved ASOP recognition with a dedicated detailed OCR pass.
- Ship names now support small OCR errors instead of requiring exact matches.
- Expanded ASOP heading and ship status recognition.
- Added regression tests for balance and fleet OCR.
