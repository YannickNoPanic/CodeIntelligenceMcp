# Taak 15 — Partial classes: handgeschreven declaratie-locatie

**Status:** open. **Plansectie:** "Task 15". **Audit-bevinding:** C7 — een
partial type wordt geïndexeerd op de EERSTE source-locatie; file/line kan naar
een gegenereerde helft wijzen (bijv. `obj/*.g.cs`).

## Bestanden

- `src/CodeIntelligenceMcp.Roslyn/RoslynWorkspaceIndex.cs` — twee plekken met
  `type.Locations.FirstOrDefault(l => l.IsInSource)`: in `BuildAsync` (regel ~150)
  en in `CreateForTesting` (regel ~92).
- `tests/CodeIntelligenceMcp.Tests/RoslynLookupTests.cs` (uitbreiden)

## Implementatie (TDD)

1. **Helper:** `internal static Location? PickPrimaryLocation(INamedTypeSymbol type)`
   — van de source-locaties: eerst degene waarvan het bestandspad NIET eindigt
   op `.g.cs`/`.generated.cs` en NIET `/obj/` of `\obj\` bevat; fallback de
   eerste source-locatie.
2. Beide callsites vervangen door de helper. Let op: de bestaande
   `.razor.g.cs`-normalisatie in `BuildAsync` (regel ~157) blijft daarna nodig
   voor Blazor-only types die alleen een gegenereerde locatie hebben.
3. **Test:** twee syntax trees met `partial class Split`, paths
   `C:\repo\obj\Split.g.cs` en `C:\repo\src\Split.cs` (gebruik
   `BuildIndexWithRoot(@"C:\repo", ...)` zodat je meteen het relatieve pad kunt
   asserten): `index.GetType("Split")!.FilePath` eindigt op `src/Split.cs`.
   NB: beide trees moeten in DEZELFDE compilation zitten (zelfde projectName in
   de helper) anders is het geen partial.

## Acceptatie

- Nieuwe + bestaande tests groen.
- Commit: `fix: partial types report the hand-written declaration location`
