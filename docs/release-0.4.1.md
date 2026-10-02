# SC NEXUS 0.4.1

## English

This update makes overlay controls clearer and lets SC NEXUS keep monitoring when its main window is closed.

- Replaced the fixed shortcut list with direct keyboard capture. Click the field and press any available key or combination, including `Ctrl`, `Alt`, `Shift`, or `Win` modifiers. No shortcut is assigned by default.
- Restyled the opacity slider and password fields to match the rest of the interface. Opacity now changes continuously and shows its exact percentage.
- Closing the main window now minimizes SC NEXUS to the system tray. Double-click the icon to restore it; the tray menu contains **Open SC NEXUS** and **Exit**.
- Silent application updates still perform a full exit before the installer starts. The app does not create a Windows startup entry.
- Added shortcut parsing and UI regression coverage; all 72 automated tests pass.

## Русский

В этом обновлении управление оверлеем стало понятнее, а SC NEXUS продолжает мониторинг после закрытия главного окна.

- Список готовых биндов заменён прямой записью с клавиатуры. Нажмите поле и введите любую свободную клавишу или сочетание с `Ctrl`, `Alt`, `Shift` либо `Win`. По умолчанию бинд не назначен.
- Ползунок прозрачности и поля паролей оформлены в общем стиле. Прозрачность меняется плавно, рядом показан точный процент.
- Крестик теперь сворачивает SC NEXUS в системный трей. Двойной щелчок возвращает окно; в меню значка есть **Открыть SC NEXUS** и **Выйти**.
- Тихое обновление по-прежнему полностью закрывает приложение перед запуском установщика. Запись в автозапуск Windows не создаётся.
- Добавлены проверки обработки биндов и интерфейса; проходят все 72 автоматических теста.
