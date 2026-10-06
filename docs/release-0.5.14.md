# SC NEXUS 0.5.14

## Русский

- Исправлена проверка обновлений: публичный канал больше не зависит от лимитов GitHub REST API.
- Установщик и SHA256SUMS скачиваются напрямую из GitHub Releases.
- Исправлена версия сборки: опубликованный EXE теперь получает ту же версию, что указана в VERSION.
- Исправлен повторный запрос обновления после установки из-за рассинхронизации версии приложения.

## English

- Fixed update checks: the public update channel no longer depends on GitHub REST API rate limits.
- Installer and SHA256SUMS are downloaded directly from GitHub Releases.
- Fixed build versioning: the published EXE now gets the same version as VERSION.
- Fixed repeated update prompts caused by application version mismatch.
