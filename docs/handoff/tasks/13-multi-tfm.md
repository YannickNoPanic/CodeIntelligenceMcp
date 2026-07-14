# Taak 13 — Multi-TFM projectdeduplicatie

**Status:** AFGEROND (zie STATE.md). **Plansectie:** "Task 13". **Audit-bevinding:** C5 —
MSBuildWorkspace levert bij multi-targeting één `Project` per TFM
(`Naam(net8.0)` etc.); elk type wordt dubbel geïndexeerd, `typeByFqn.TryAdd`
houdt stil de eerste, projectfilters matchen alle TFM-varianten.

## Bestanden

- `src/CodeIntelligenceMcp.Roslyn/RoslynWorkspaceIndex.cs` (`BuildAsync`-projectloop, regel ~142; `GetProjectDependencies`, regel ~409)
- `tests/CodeIntelligenceMcp.Tests/RoslynLookupTests.cs` (uitbreiden)

## Implementatie (TDD)

1. **`internal static string NormalizeProjectName(string name)`** op de index:
   strip een trailing `(...)`-groep alleen als de inhoud op een TFM lijkt
   (prefix `net`, `netstandard`, `netcoreapp`). `"X.Core(net8.0)"` → `"X.Core"`,
   `"X.Core"` blijft, `"Foo(bar)"` blijft.
2. Test (Theory) zoals in de plansectie; InternalsVisibleTo bestaat al.
3. In `BuildAsync`: `HashSet<string> seenProjectFiles` (OrdinalIgnoreCase,
   op `project.FilePath ?? project.Name`); `continue` bij duplicaat — alleen de
   eerste TFM-variant wordt geïndexeerd. Gebruik `NormalizeProjectName(project.Name)`
   voor `IndexedType.ProjectName`.
4. Zelfde normalisatie in `GetProjectDependencies` (project-nodes en edges),
   met deduplicatie van edges na normalisatie.

## Acceptatie

- Nieuwe Theory + bestaande tests groen.
- Commit: `fix: deduplicate multi-targeted projects and strip TFM suffixes`
