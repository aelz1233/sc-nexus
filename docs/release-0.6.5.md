# SC NEXUS 0.6.5

## English
- Fixed update checks on networks where `api.github.com` is unreachable but `github.com` is available. The updater now uses GitHub's latest published release page as a fallback and downloads its installer and checksums from the public release.
- Installer downloads still require a matching SHA-256 checksum. Saved repository tokens are never sent to the public fallback endpoint.

## Русский
- Исправлена проверка обновлений, когда `api.github.com` недоступен, но `github.com` работает. Программа использует страницу последнего опубликованного релиза и скачивает с неё установщик и контрольные суммы.
- Перед установкой по-прежнему проверяется SHA-256. Сохранённый токен не отправляется через запасной публичный адрес.
