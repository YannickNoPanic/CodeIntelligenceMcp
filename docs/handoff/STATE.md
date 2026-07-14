# STATE — stand van zaken audit-fixes

Laatst bijgewerkt: 2026-07-13. Branch: `feature/audit-fixes` (basis: `main`).
Testsuite bij handoff: **133/133 groen**.

## Afgerond (met commit)

| Taak | Commit | Wat |
|---|---|---|
| baseline | `ee7c313` | Lazy-loading refactor vorige sessie + auditrapport + plan |
| 1. ToolResponses | `fd6502e` | Gedeelde serializer: camelCase, relaxed escaping, `OkList`-envelope, `Err(message, hint, detail)`. Alle 10 tool-classes gemigreerd. |
| 2. WorkspaceAccess | `506a851` | Eén foutgrens: unknown-workspace noemt bekende namen; `WorkspaceLoadException` (hint+detail) voor MSBuild-not-found en solution-load-failures; nooit stack traces naar de client. |
| 3. list_workspaces | `57d14bf` | Nieuwe tool + `IWorkspaceProvider.IsLoaded`. |
| 4. Zoekkwaliteit | `8140886` | Glob-wildcards (`*`/`?`) in search_symbol/find_types en alle file-walk finders; compiler-gegenereerde members uit search gefilterd; `maxResults` (default 100) op alle list-tools; `hasCmdletBinding`-typo gefixt. |
| 5. Relatieve paden | zie git log | `RoslynWorkspaceIndex.Rel()`: alle response-paden solution-relatief met forward slashes; interne opslag blijft absoluut; ViolationDetector relativiseert centraal; ChangeAnalyzer vergelijkt in relatieve vorm. |
| 6. Staleness | zie git log | `IsStaleCached()` (10s TTL); alle CSharpTools/DiagnosticsTool-responses dragen een `stale`-vlag; wiki-banner gebruikt de cache; analyze_changes houdt het precieze `IsStale()`. |
| 7. Git worktrees | zie git log | `.git`-als-file herkend; unborn-HEAD guards; base-branch fallback main → origin/main → master → origin/master → tracked branch; error noemt beschikbare branches. Tests in `GitDiffServiceTests.cs`. |
| 8. Build-cancellation | zie git log | Gedeelde index-build draait op `CancellationToken.None`; callers verlaten via `WaitAsync(ct)`. |
| 9. Log-rotatie | zie git log | Rol naar `.old` boven 10MB (IOException-tolerant voor tweede instantie); exceptions loggen volledige `ToString()`; timestamps InvariantCulture met ms. |
| 10. Config discovery | zie git log | `--config`-arg → `CODEINTEL_CONFIG`-env → naast binary; missing config = warning + lege lijst (niet fataal); invalid JSON = duidelijke fout met pad; csproj-Content conditioneel — clean clone bouwt. |
| 11. Derived types | zie git log | `find_derived_types`-tool (BaseType-ketens, elke diepte); `implementsInterface`/`find_implementations` accepteren type-args (`IUseCase<ReqA, int>`, incl. keyword-vormen zoals `int`). |
| 12. Transitieve callers | zie git log | `find_callers` heeft `depth` (1..3, BFS, frontier-cap 200/level), zoekt alle ordinary overloads, results dragen `depth`. E2E-dekking hoort in taak 18. |
| 13. Multi-TFM | zie git log | Dedup per csproj in BuildAsync; `NormalizeProjectName` stript TFM-suffixen in index, dependency-graph en cycle-detectie. |
| 14. .slnf | zie git log | `SolutionFilterFile.Parse` + allowlist in BuildAsync; onbekende extensies falen met actionable error; descriptions noemen .slnf. |
| 15. Partial classes | zie git log | `PickPrimaryLocation` prefereert niet-gegenereerde declaratie (geen obj/, .g.cs, .generated.cs). |
| 16. Ad-hoc cache | zie git log | Absoluut pad naar geconfigureerde workspace hergebruikt diens config en cache key. |
| 17. Kleine fixes | zie git log | NSubstitute verwijderd, Tomlyn gepind op 0.17.0 (typo was al in taak 4 gedaan). |

Controleer met `git log --oneline main..feature/audit-fixes`.

## Open

Taken 18 t/m 21 — zie de tabel in [README.md](README.md) en de per-taak-files
in `tasks/`.

## Belangrijke afwijkingen t.o.v. het originele plan

Het plan (`docs/superpowers/plans/2026-07-07-audit-fixes.md`) is geschreven
vóór de implementatie. De volgende dingen zijn anders uitgepakt — per-taak-files
noemen ze ook, maar dit is het overzicht:

1. **Config-records zijn `public`** (`McpConfig`, `WorkspaceConfig`,
   `CleanArchitectureConfig` in `src/CodeIntelligenceMcp/Config/McpConfig.cs`) —
   nodig omdat publieke tool-constructors ze injecteren.
2. **Tool-prologue-patroon** is overal:
   ```csharp
   (XIndex? index, string? error) = await WorkspaceAccess.GetAsync(provider, config, "<type>", workspace, ct);
   if (index is null)
       return error!;
   ```
   Elke tool-class heeft `McpConfig config` als primary-constructorparameter.
3. **`SymbolQueryMatcher`** (Roslyn) en **`NameMatcher`** (Common) zijn bewust
   gedupliceerd (~20 regels): `.Roslyn` mag niet van `.Common` afhangen.
4. **`CreateForTesting`** heeft een optionele `rootDir`-parameter gekregen
   (voor Rel()-tests).
5. **`ViolationDetector.DetectAsync`** relativiseert nu centraal; de
   rule-methodes zelf werken met absolute paden. Nieuwe rules moeten dus
   NIETS aan paden doen — de wrapper regelt het.
6. **`GlobalUsings.cs`** van het serverproject bevat nu ook
   `CodeIntelligenceMcp.Config`.
7. De **`variables`-sectie** in mcp-config.json is dead config (geen substitutie
   geïmplementeerd; bewuste keuze, zie project-CLAUDE.md).

## Niet aanraken zonder de gebruiker

- Licentie/LICENSE-file, toolnaam, `dotnet tool`-packaging, CI-pipeline
  (bewust geparkeerd — beslissing ligt bij de gebruiker).
- `src/CodeIntelligenceMcp/Properties/PublishProfiles/` — door de gebruiker
  aangemaakte VS-publish-profielen, bewust untracked gelaten.
- SSE-mode en het nep-OAuth-endpoint in Program.cs — documenteren als
  local-dev-only mag (taak 21), verwijderen niet.
