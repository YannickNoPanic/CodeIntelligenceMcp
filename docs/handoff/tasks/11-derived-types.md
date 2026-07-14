# Taak 11 — find_derived_types + generic-aware interface-matching

**Status:** AFGEROND (zie STATE.md). **Plansectie:** "Task 11". **Audit-bevindingen:** C3
(geen derived-types-query; subclass-hiërarchieën onzichtbaar) en C4
(`implementsInterface` matcht op simple name; `IUseCase<TIn>` vs
`IUseCase<TIn,TOut>` niet te onderscheiden, query mét type-args faalt).

## Bestanden

- `src/CodeIntelligenceMcp.Roslyn/RoslynWorkspaceIndex.cs`
- `src/CodeIntelligenceMcp/Tools/CSharpTools.cs` (nieuwe tool)
- `tests/CodeIntelligenceMcp.Tests/RoslynLookupTests.cs` (uitbreiden)

## Implementatie (TDD — volledige testcode in plansectie Task 11)

1. **`FindDerivedTypes(string baseTypeName)`** op de index: loop over
   `_allTypes`, walk per type de `BaseType`-keten
   (`for (var b = t.Symbol.BaseType; b is not null; b = b.BaseType)`), match op
   simple name (OrdinalIgnoreCase) of FQN. Retourneer `ImplementationSummary`
   met `Rel(t.FilePath)` — zie CONVENTIONS.md over paden.
2. **Generic-aware matching** in `FindTypes` (implementsInterface) én
   `FindImplementations`: als de query `<` bevat, parse als naam + comma-split
   args binnen `<...>`; kandidaat-interface `i` matcht wanneer
   `i.Name == parsedName && i.TypeArguments.Length == args.Length` en elke arg
   op simple name matcht (trim, OrdinalIgnoreCase). Zonder `<` blijft het
   huidige simple-name-gedrag. Extraheer als private static helper, gebruikt
   door beide methodes.
3. **Nieuwe tool** `find_derived_types(workspace, baseTypeName, maxResults=100)`
   → `ToolResponses.OkList`. Volg exact het patroon van `find_implementations`
   in CSharpTools.cs (WorkspaceAccess-prologue, zie CONVENTIONS.md).

## Tests

- `FindDerivedTypes_AbstractBase_ReturnsAllDescendants` — 3-laags hiërarchie,
  assert transitief (EmailHandler én SmsHandler bij base BaseHandler).
- `FindTypes_GenericInterfaceWithArgs_MatchesExactConstruction` —
  `IUseCase<ReqA, int>` matcht alleen UseCaseA.
- Gebruik de bestaande `BuildIndex`-helper in RoslynLookupTests.cs.

## Acceptatie

- Nieuwe + bestaande tests groen.
- Commit: `feat: find_derived_types tool and generic-argument-aware interface matching`
