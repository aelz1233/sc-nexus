# SC NEXUS 0.5.10

## Русский

- Исправлено распознавание баланса на полном экране mobiGlas: если маленькая подпись «Главная» не читается, Nexus сначала увеличивает нижнюю панель.
- Учтена ошибка OCR в окончании слова «Главная». По-прежнему проверяются расположение кошелька, контекст mobiGlas и два совпадающих чтения суммы.
- Проверено на полном кадре игры и исходном обрезанном изображении. Автоматические тесты включают русский и английский варианты подписи.

## English

- Fixed balance recognition on full-screen mobiGlas: when the small Home label is unreadable, Nexus enlarges the bottom toolbar first.
- Tolerates an OCR error in the Russian Home label. Wallet position, mobiGlas context and two matching readings are still required.
- Checked against a full game frame and the original cropped image. Automated tests cover Russian and English Home labels.
