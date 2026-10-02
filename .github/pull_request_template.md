## Summary

Explain the user-visible change and why it is needed.

## Verification

- [ ] Ran `dotnet test SCNexus.slnx -c Release`
- [ ] Added or updated focused tests where behavior changed
- [ ] Checked Russian and English UI text when visible text changed
- [ ] Confirmed no token, private log, local database, or build artifact is included

## Star Citizen safety

- [ ] The change remains external and read-only: no injection, memory reading, hooks, packet interception, or game-file writes
