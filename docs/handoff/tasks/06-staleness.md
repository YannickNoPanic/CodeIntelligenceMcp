# Taak 6 — Staleness op alle dotnet-tool-responses

**Status:** open. **Plansectie:** "Task 6" in `docs/superpowers/plans/2026-07-07-audit-fixes.md`.
**Audit-bevinding:** R2 — staleness wordt maar in 2 van ~50 tools gecheckt; `get_method` serveert stil verouderde bodies terwijl de agent code wijzigt.

## Doel

Elke dotnet-tool-response draagt een `stale`-vlag wanneer de git-fingerprint
afwijkt van die bij indexbouw, zonder per call een dure git-status te betalen.

## Implementatie

1. `RoslynWorkspaceIndex` krijgt `public bool IsStaleCached()`:
   - velden: `private readonly object _staleLock = new(); private DateTime _staleCheckedAtUtc; private bool _lastStale;`
   - binnen de lock: herbereken via bestaand `IsStale()` alleen als
     `DateTime.UtcNow - _staleCheckedAtUtc > TimeSpan.FromSeconds(10)`, cache het resultaat.
   - Let op: `IsStale()` doet een volledige `RetrieveStatus` (duur op grote repos) — daarom de TTL.
2. `CSharpTools` + `DiagnosticsTool`: elke succes-return wordt
   `ToolResponses.Ok(x, index.IsStaleCached())` resp.
   `ToolResponses.OkList(x, maxResults, index.IsStaleCached())`.
   De infrastructuur hiervoor bestaat al — `Ok`/`OkList` hebben al een `stale`-parameter.
3. `CodebaseWikiTool`: banner blijft, maar switch `index.IsStale()` → `index.IsStaleCached()`.
4. `ChangeAnalysisTool`: NIET wijzigen — die gebruikt bewust het echte `IsStale()`
   (correctheid van de auto-refresh gaat boven kosten).

## Tests

Unit-testen van de TTL kan zonder git door `IsStaleCached` niet te testen op
git-gedrag maar alleen compile-groen te houden; de git-afhankelijke assertie
komt in de fixture-integratietests (taak 18). Geen aparte failing test vereist.

## Acceptatie

- Alle bestaande tests groen.
- `get_type` op een workspace met een gewijzigde file toont `"stale": true`
  (handmatig of via taak 18-fixture verifieerbaar).
- Commit: `feat: stale-index warnings on all dotnet tool responses`
