# SC NEXUS 0.5.7

## Русский

- Дополнено распознавание русских статусов ASOP: «Возместить», «Доступно», «Извлечь». Ранее такие строки могли пропускаться как нераспознанные.
- «Заблокировано» имеет приоритет над «Доступно» и кнопкой «Извлечь» в той же строке: такой корабль не добавляется.
- Сохранены автоматическая прокрутка ASOP, остановка по Esc, устранение повторов и определение категории по UEX из 0.5.6. Нечитаемые статусы пропускаются, скан может быть неполным.

## English

- Recognize additional Russian ASOP labels for Claim, Available and Retrieve that previously caused rows to be skipped.
- Locked takes precedence over Available and Retrieve within the same row, excluding that ship entry.
- Includes automatic ASOP scrolling, Esc cancellation, deduplication and UEX categories from 0.5.6. Unreadable statuses are skipped and scan results may be incomplete.
