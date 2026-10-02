# Contributing to SC Nexus

Thanks for helping improve SC Nexus. The application is a Windows companion for Star Citizen, so correctness, data provenance, and non-invasive behavior matter more than adding UI volume.

## Before opening an issue

- Search existing issues first.
- Include the SC Nexus version, Windows version, and Star Citizen environment when relevant.
- For a data issue, say which source was involved: `Game.log`, local files, UEX, Star Citizen Wiki, OCR, or saved history.
- Do not attach tokens, passwords, full account data, or private game logs. Remove sensitive values before sharing excerpts.

## Pull requests

1. Describe the user-visible outcome and the reason for the change.
2. Keep the change focused; avoid unrelated formatting or refactoring.
3. Preserve the external, read-only relationship with Star Citizen. Do not introduce injection, memory reading, hooks, packet interception, or game-file writes.
4. Add or update a focused test for changed behavior where practical.
5. Run `dotnet test SCNexus.slnx -c Release` before submitting.

## Data providers

New providers should implement `IDataProvider`, return a source, timestamp, and confidence for values, and behave safely when a source is unavailable. Never replace a more reliable observation with a lower-priority fallback.

## Reporting security concerns

Do not open a public issue for a suspected exposed secret or a security-sensitive defect. Contact the repository owner privately through GitHub instead.
