# Taak 14 — .slnf solution-filter-support

**Status:** open. **Plansectie:** "Task 14". **Audit-bevinding:** C6 — `.slnf`
wordt niet ondersteund en niet gevalideerd; het pad gaat ongecheckt naar
`OpenSolutionAsync`. Juist grote solutions (de doelgroep) gebruiken filters.

## Bestanden

- Nieuw: `src/CodeIntelligenceMcp.Roslyn/SolutionFilterFile.cs`
- `src/CodeIntelligenceMcp.Roslyn/RoslynLoader.cs`
- `src/CodeIntelligenceMcp.Roslyn/RoslynWorkspaceIndex.cs` (`BuildAsync` krijgt optionele allowlist)
- Alle tool-`[Description]`s: `.sln/.slnx` → `.sln/.slnx/.slnf` (mechanische find/replace over `Tools/*.cs`)
- Nieuw: `tests/CodeIntelligenceMcp.Tests/SolutionFilterFileTests.cs`

## Implementatie (TDD — testcode in plansectie Task 14)

1. **Parser:**
   ```csharp
   public sealed record SolutionFilterFile(string SolutionPath, IReadOnlySet<string> ProjectPaths)
   {
       public static SolutionFilterFile Parse(string slnfPath);
   }
   ```
   .slnf-JSON: `{ "solution": { "path": "..\\X.sln", "projects": ["src\\A\\A.csproj"] } }`.
   Paden absoluut resolven tegen de .slnf-locatie, forward slashes,
   OrdinalIgnoreCase-set. Backslashes in de JSON zijn escaped (`\\`).
2. **`RoslynWorkspaceIndex.BuildAsync(..., IReadOnlySet<string>? projectAllowlist = null, ...)`:**
   in de projectloop: skip projecten waarvan het genormaliseerde
   `project.FilePath` niet in de set zit.
3. **`RoslynLoader.LoadAsync`:** eindigt het pad op `.slnf` (OrdinalIgnoreCase):
   `Parse`, open de verwezen `.sln`, geef de allowlist door. Onbekende extensies
   (niet .sln/.slnx/.slnf) → `WorkspaceLoadException($"'{path}' is not a .sln, .slnx, or .slnf file")`.
   NB: de fingerprint (`ComputeFingerprint(solutionPath)`) moet het .slnf-pad
   blijven gebruiken voor root-resolutie — dat werkt omdat die alleen omhoog
   wandelt naar `.git`.
4. Let op de wisselwerking met taak 13: de allowlist-check komt NAAST de
   TFM-dedup in dezelfde loop.

## Acceptatie

- Parse-test + bestaande tests groen.
- Commit: `feat: .slnf solution filter support`
