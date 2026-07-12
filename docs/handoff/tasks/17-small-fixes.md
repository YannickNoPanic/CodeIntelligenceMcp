# Taak 17 — Kleine fixes (restant)

**Status:** half af. **Plansectie:** "Task 17".

## Al gedaan (in taak 4, commit 8140886)

- `hassCmdletBinding` → `hasCmdletBinding` serialisatie-typo in
  `PowerShellTools.cs`. NIET opnieuw doen.

## Nog te doen

1. **NSubstitute verwijderen** uit
   `tests/CodeIntelligenceMcp.Tests/CodeIntelligenceMcp.Tests.csproj`
   (PackageReference regel ~15). Verifieer eerst met grep dat er nul
   `Substitute.For`-usages zijn (bij handoff: nul; fakes zijn inline classes).
2. **Tomlyn pinnen** in
   `src/CodeIntelligenceMcp.Python/CodeIntelligenceMcp.Python.csproj`:
   floating `0.17.*` vervangen door de daadwerkelijk geresolvede versie —
   check `src/CodeIntelligenceMcp.Python/obj/project.assets.json` voor het
   nummer en pin exact die.

## Acceptatie

- `dotnet test` groen na beide wijzigingen (restore draait mee).
- Commit: `chore: drop unused NSubstitute, pin Tomlyn`
