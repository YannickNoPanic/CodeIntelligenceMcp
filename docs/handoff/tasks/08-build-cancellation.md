# Taak 8 — Gedeelde index-build ontkoppelen van eerste caller

**Status:** open. **Plansectie:** "Task 8". **Audit-bevinding:** R7 — het
CancellationToken van de EERSTE caller stuurt de gedeelde `Lazy<Task>`-build;
als agent A annuleert (timeout/Esc) crasht de build waar agent B op wacht en
begint de volgende call van nul.

## Bestand

`src/CodeIntelligenceMcp/Workspaces/WorkspaceProviderBase.cs` (regel ~54-56).

## Implementatie

In `GetAsync`, de Lazy-factory:

```csharp
Lazy<Task<TIndex>> lazy = _loaded.GetOrAdd(
    cacheKey,
    _ => new Lazy<Task<TIndex>>(() => LoadAsync(ws, CancellationToken.None)));
```

(was: `LoadAsync(ws, ct)`). Callers verlaten hun wachttijd al via
`WaitAsync(ct)`; de build zelf loopt nu altijd door zodat parallelle agents
een in-flight index nooit kwijtraken aan andermans timeout.

Werk het comment boven de factory bij (het beschrijft nu nog het oude gedrag
"The first caller's token drives the shared build").

De bestaande evictie van faulted builds in de catch-blokken blijft ongewijzigd
— die is nodig voor retry na echte failures.

## Acceptatie

- Bestaande tests groen (er is geen unit-test voor dit gedrag; de
  fixture-integratietest van taak 18 kan het desgewenst dekken).
- Commit: `fix: shared index build no longer cancelled by first caller`
