# Handoff — Audit Fixes CodeIntelligenceMcp

Entry point voor elk model/LLM dat dit werk oppakt. Lees in deze volgorde:

1. **Dit bestand** — context en werkwijze
2. **[STATE.md](STATE.md)** — wat is af, wat staat open
3. **[CONVENTIONS.md](CONVENTIONS.md)** — verplichte patronen en valkuilen (lezen vóór je iets aanraakt)
4. **`docs/superpowers/plans/2026-07-07-audit-fixes.md`** — het originele volledige plan met code per taak
5. **`docs/handoff/tasks/*.md`** — per open taak: status, afwijkingen t.o.v. het plan, acceptatie

## Wat is dit project

Een .NET 10 MCP-server (stdio) die C#/Roslyn-, Classic ASP-, JS/TS-, Python- en
PowerShell-codebases indexeert voor AI-agents. Kernbelofte: token-efficiënte,
gestructureerde antwoorden i.p.v. `Search + Read × N`.

Er ligt een productaudit (`docs/product-audit-2026-07-06.md`) met bevindingen.
De opdracht van de gebruiker: **alle technische bevindingen oplossen** ("de tool
het beste ooit maken"). Licentiekeuze en toolnaam zijn bewust geparkeerd —
niet oppakken zonder de gebruiker.

## Werkwijze (verplicht)

- Branch: `feature/audit-fixes`. Eén taak = één commit (conventional commits,
  afsluiten met `Co-Authored-By: Claude <model> <noreply@anthropic.com>`).
- TDD waar het plan dat voorschrijft: eerst failing test, dan implementatie.
- Testcommando:
  ```
  dotnet test tests/CodeIntelligenceMcp.Tests -v q --nologo
  ```
  Alle bestaande tests (127 bij handoff) moeten groen blijven.
- **Build-lock valkuil:** er draaien vaak live MCP-serverprocessen die de
  output-DLL's locken. NOOIT processen killen. Gebruik een geïsoleerde output-dir:
  ```
  dotnet test tests/CodeIntelligenceMcp.Tests -v q --nologo -o <tempdir>\testout
  ```
- Volg CLAUDE.md (repo-root) voor codestijl. Belangrijkste: file-scoped
  namespaces, primary constructors, geen emoji's, geen TODO-comments,
  structured logging.

## Taakvolgorde

Open taken in aanbevolen volgorde (nummers verwijzen naar het originele plan):

| # | Taak | File | Effort |
|---|---|---|---|
| 6 | Staleness op alle dotnet-tools | [tasks/06-staleness.md](tasks/06-staleness.md) | S |
| 7 | Git worktree-fix + branch-fallback | [tasks/07-git-worktrees.md](tasks/07-git-worktrees.md) | S |
| 8 | Build-cancellation ontkoppelen | [tasks/08-build-cancellation.md](tasks/08-build-cancellation.md) | XS |
| 9 | Log-rotatie + stack traces | [tasks/09-log-rotation.md](tasks/09-log-rotation.md) | S |
| 10 | Config discovery + first-run | [tasks/10-config-discovery.md](tasks/10-config-discovery.md) | S |
| 11 | find_derived_types + generic interfaces | [tasks/11-derived-types.md](tasks/11-derived-types.md) | M |
| 12 | Transitieve find_callers | [tasks/12-transitive-callers.md](tasks/12-transitive-callers.md) | M |
| 13 | Multi-TFM dedup | [tasks/13-multi-tfm.md](tasks/13-multi-tfm.md) | S |
| 14 | .slnf-support | [tasks/14-slnf.md](tasks/14-slnf.md) | M |
| 15 | Partial classes locatie | [tasks/15-partial-classes.md](tasks/15-partial-classes.md) | S |
| 16 | Ad-hoc pad hergebruikt cache | [tasks/16-adhoc-cache.md](tasks/16-adhoc-cache.md) | XS |
| 17 | Kleine fixes (NSubstitute, Tomlyn) | [tasks/17-small-fixes.md](tasks/17-small-fixes.md) | XS |
| 18 | Fixture solution + integratietests | [tasks/18-fixture-integration-tests.md](tasks/18-fixture-integration-tests.md) | L |
| 19 | ViolationDetector-tests | [tasks/19-violation-tests.md](tasks/19-violation-tests.md) | M |
| 20 | Analyzer-tests | [tasks/20-analyzer-tests.md](tasks/20-analyzer-tests.md) | M |
| 21 | Docs-waarheidspas | [tasks/21-docs.md](tasks/21-docs.md) | M |

Taken 6-10 en 13-17 zijn onderling onafhankelijk. Taak 18 pas na 7/11-15
(integratie test het eindgedrag). Taak 21 als allerlaatste.

## Afronding

Na de laatste taak: volledige testrun, daarna aan de gebruiker vragen hoe te
integreren (merge naar main / PR). Niet zelf naar main pushen.
