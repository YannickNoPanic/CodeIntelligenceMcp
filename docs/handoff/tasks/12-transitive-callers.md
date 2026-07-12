# Taak 12 — Transitieve find_callers + alle overloads

**Status:** open. **Plansectie:** "Task 12". **Audit-bevinding:** C1 —
`find_callers` doet één SymbolFinder-pass (alleen directe callers) en pakt
alleen de EERSTE ordinary overload (`ReferenceQueries.cs` regel ~70-72).

## Bestanden

- `src/CodeIntelligenceMcp.Roslyn/ReferenceQueries.cs` (`FindCallersAsync`)
- `src/CodeIntelligenceMcp.Roslyn/Models/CallerResult.cs` (veld `int Depth` toevoegen)
- `src/CodeIntelligenceMcp/Tools/CSharpTools.cs` (`find_callers` krijgt `depth`-param)

## Implementatie

1. Signature: `FindCallersAsync(string typeName, string methodName, int depth = 1, CancellationToken ct = default)`.
2. **Alle overloads:** union van `SymbolFinder.FindReferencesAsync` over elke
   ordinary overload van de methode, niet alleen de eerste.
3. **BFS voor diepte:** niveau 1 = huidige logica. Voor niveau 2..depth: neem
   per gevonden caller het `IMethodSymbol` van de omsluitende methode (de
   bestaande syntax-walk die callerType/callerMethod bepaalt uitbreiden zodat
   die ook het symbool teruggeeft via het semantic model:
   `compilation.GetSemanticModel(tree).GetDeclaredSymbol(methodDecl)`), en run
   daarop opnieuw FindReferencesAsync.
4. Dedupliceer op `(callerType, callerMethod, file, line)`. Cap de BFS-frontier
   op 200 methodes per niveau. `Depth` = 1 voor directe callers.
5. Tool-param: `[Description("Transitive depth: 1 = direct callers only (default), up to 3 = callers-of-callers")] int depth = 1,`
   met `Math.Clamp(depth, 1, 3)` en doorgeven aan FindCallersAsync.
6. Paden blijven via `index.Rel(...)` gaan (bestaat al in de results-mapping).

## Tests

`CreateForTesting` heeft geen `Solution`, dus SymbolFinder werkt daar niet.
Dektest komt via de fixture-solution (taak 18): een keten
`Caller -> GreetUseCase.Greet` bestaat daar; voeg voor depth=2 een extra
schakel toe (bijv. `Outer.Run()` die `Caller.Invoke()` aanroept) en assert dat
depth=2 de Outer-frame vindt met `Depth == 2`.
Bij deze taak zelf: alleen bestaande tests groen houden; noteer in de
taak-18-file dat de depth-test daar hoort (staat er al).

## Acceptatie

- Bestaande tests groen; `CallerResult` serialiseert `depth` mee.
- Commit: `feat: transitive find_callers with depth and all-overload coverage`
