# Product Audit — Code Intelligence MCP

**Datum:** 2026-07-06
**Scope:** huidige working tree (inclusief niet-gecommitte wijzigingen), commit-basis `686f3bb`
**Rol:** kritische externe reviewer — is dit product-klaar voor .NET-teams met AI-agents?

## Aannames (in plaats van verduidelijkende vragen)

1. De audit betreft de huidige working tree, inclusief de omvangrijke niet-gecommitte refactoring; die staat wordt behandeld als "het product".
2. De MCP-tools van deze server indexeren datalake2/datalake1 — niet deze repo zelf. De code-audit is daarom via gerichte file-reads gedaan; het runtime-gedrag (foutkwaliteit, response-omvang, wildcard-gedrag) is live getest tegen de draaiende server.
3. Token-schattingen hanteren ~4 tekens per token.
4. Effort-schaal: S = minder dan één avond, M = 1–3 avonden, L = meer dan 3 avonden.

---

## 1. Samenvatting

De technische kern is beter dan verwacht voor interne tooling: thread-safe lazy loading, immutable indexes, LibGit2Sharp in plaats van git-op-PATH, nette `{"error"}`-contracten, 101 groene tests. Maar het is vandaag **niet verkoopbaar en zelfs niet extern testbaar**: er is geen licentie, geen packaging, geen CI, de first-run op een schone clone is stuk, en het is in de praktijk Windows-only. Pijnlijker: de kernbelofte (token-efficiëntie) wordt door de eigen tools ondermijnd — een brede `search_symbol` gaf live een respons van 836k tekens (~200k tokens) zonder cap. En het paradepaardje-scenario (parallelle agents in git worktrees) breekt op een `.git`-als-file-bug.
**Afstand tot eerste externe tester: ± 8–10 avonden.** **Afstand tot eerste betalende klant: ± 25 avonden** (staleness, cold start, cross-platform, benchmark, docs). Niets hiervan is fundamenteel — het is allemaal afwerking, maar het is wél allemaal nodig.

---

## 2. Bevindingen-tabel

Severity: `blocker` = kan zo niet verkocht worden · `belangrijk` = eerste betalende klant loopt er direct tegenaan · `nice-to-have`.

### Licentie, packaging & distributie

| # | Bevinding | Severity | Effort |
|---|---|---|---|
| P1 | Geen LICENSE in de repo-root; niemand mag dit legaal gebruiken. BSD-3-attributie voor de VBScript.Parser-fork (`kmvi`) ontbreekt bij de gekopieerde code in `src/VBScript.Parser/` | blocker | S |
| P2 | Geen enkele packaging-metadata: geen `PackAsTool`, `ToolCommandName`, `PackageId`, `Version`, auteur of licentie-tag in enige csproj. Entrypoint gebruikt `Microsoft.NET.Sdk.Web` (voor de SSE-tak), wat `dotnet tool`-packaging compliceert | blocker | M |
| P3 | Geen CI: `.github/workflows/` is leeg, geen azure-pipelines.yml, geen versioning-strategie. Distributie is nu "clone, publish lokaal" | blocker | M |
| P4 | First-run op schone clone is stuk: `mcp-config.json` is gitignored maar de csproj declareert hem als `<Content>` — stap 1 van de README (publish) faalt met MSB3030 vóór stap 2 (config) aan bod komt. README noemt `mcp-config.example.json` nergens | blocker | S |
| P5 | Config wordt uitsluitend uit `AppContext.BaseDirectory` geladen (`Program.cs:27`) — geen CLI-arg of env-var. Na publish overschrijft elke re-publish de config; edits aan de repo-root-file doen niets | blocker | S |
| P6 | Windows-only in de praktijk, nergens vermeld: `-r win-x64` publish, `.exe`-registratie, `%TEMP%`-logpad, "Visual Studio of MSBuild" als prerequisite. Geen macOS/Linux/devcontainer-verhaal, geen `global.json` om SDK-drift te beheersen | blocker | M |
| P7 | Persoonlijke paden en klantnamen in getrackte docs: `TASK.md`, `docs/TOOLS.md`, `CLAUDE.md` bevatten `C:/Git/Datalake2.0/...`, `datalake2`, `WR_Development_datalake_portal`. Ook "Datalake2.Core" als voorbeeldtekst in tool-descriptions (`CodebaseWikiTool.cs:12`, `DiagnosticsTool.cs:16`) die elke MCP-client ziet | blocker (voor open-source) | S |
| P8 | 9 interne .md-bestanden in de root (TASK, PLAN×3, ARCHITECTURE, DEPENDENCIES, EXAMPLES, …), deels stale (TASK.md beschrijft indexing-on-startup en een geschrapt `${VAR}`-mechanisme; CLAUDE.md noemt MCP 1.2.0/Roslyn 4.13.0, de tree pint 1.1.0/5.3.0) | belangrijk | S |
| P9 | Tomlyn heeft een floating version (`0.17.*`); geen central package management; NSubstitute is een dode dependency in het testproject | nice-to-have | S |

### Token-efficiëntie (de kernbelofte)

| # | Bevinding | Severity | Effort |
|---|---|---|---|
| T1 | **Live gemeten:** `search_symbol("UseCase")` op datalake2 → 835.842 tekens (~200k tokens) in één respons. Geen cap, geen paging, geen truncated-indicator — op `search_symbol`, `find_usages`, `find_types`, `find_violations`, `asp_search` en alle `*_search`-tools | blocker | S–M |
| T2 | `search_symbol` retourneert compiler-gegenereerde members (`get_EqualityContract`, `EqualityContract`, record-ctors) als losse hits — pure ruis, live geverifieerd | blocker (onderdeel kernbelofte) | S |
| T3 | Elke hit bevat het volledige absolute pad (`C:\\Git\\Datalake2.0\\src\\...`), dubbel-ge-escaped in JSON — bij honderden hits het grootste deel van de tokens. Workspace-relatieve paden zouden 60–70% schelen | belangrijk | S |
| T4 | Wildcard-query's falen stil: `Get*UseCase` → `[]` (live getest) — de `*` wordt letterlijk gematcht, zonder hint dat search substring-based is. Agent verspilt een round trip | belangrijk | S |
| T5 | JSON-encoder escapet quotes als `'` — kleine maar structurele token-ruis in elke error/tekstrespons | nice-to-have | S |

### Tool-coverage & Roslyn-diepgang

| # | Bevinding | Severity | Effort |
|---|---|---|---|
| C1 | Geen transitieve call hierarchy — `find_callers` doet één `SymbolFinder`-pass, alleen directe callers, en pakt alleen de **eerste overload** (`ReferenceQueries.cs:70-72`) | belangrijk | M |
| C2 | Geen rename-impactanalyse; dichtstbijzijnde proxy's zijn `find_usages` (alleen locaties) en `get_change_risk` (numeriek) | belangrijk | M |
| C3 | Type hierarchy is half: alleen directe `BaseType` en directe interfaces (`symbol.Interfaces`, niet `AllInterfaces`); **geen derived-types-query** — subclass-hiërarchieën zijn onzichtbaar | belangrijk | S |
| C4 | `implementsInterface` matcht op simple name zonder generic args: `IUseCase<TIn>` en `IUseCase<TIn,TOut>` zijn niet te onderscheiden; een query mét type-argumenten faalt | belangrijk | S |
| C5 | Multi-targeting (meerdere TFM's) is onbehandeld: MSBuildWorkspace levert één project per TFM, `typeByFqn.TryAdd` houdt stil de eerste — dubbel geïndexeerd, ongedefinieerd gedrag. Datalake2 is single-TFM, klanten niet | belangrijk | M |
| C6 | `.slnf` (solution filters) niet ondersteund en niet gevalideerd — pad gaat ongecheckt naar `OpenSolutionAsync`. Juist grote solutions (de doelgroep) gebruiken slnf | belangrijk | S–M |
| C7 | Partial classes: type wordt geïndexeerd op de **eerste** source-locatie — file/line wijst naar een willekeurige declaratie. Members worden wel symbol-breed geaggregeerd | nice-to-have | S |
| C8 | Source generators: alleen Razor (`.razor.g.cs`-normalisatie); overige generator-output wordt als gewone types geïndexeerd — acceptabel, maar ongedocumenteerd | nice-to-have | S |
| C9 | Project-dependency-graph bestaat (`get_project_dependencies` + `find_circular_dependencies`) maar zonder NuGet-package-dependencies | nice-to-have | S |
| C10 | Geen `list_workspaces`-tool; de unknown-workspace-error noemt de bekende workspaces niet (die worden alleen server-side gelogd, `WorkspaceProviderBase.cs:39`) | belangrijk | S |

### Robuustheid & performance

| # | Bevinding | Severity | Effort |
|---|---|---|---|
| R1 | **Worktree-bug:** `ResolveRepoRoot` eist dat `.git` een *directory* is (`GitDiffService.cs:54`); in een git worktree is `.git` een file → staleness-detectie stil uitgeschakeld en `analyze_changes` faalt — exact het parallelle-agents-scenario waarop het product gepitcht wordt | blocker | S |
| R2 | Staleness wordt in slechts 2 van ~48 tools gecheckt (wiki-banner, analyze_changes-autorefresh). `get_method` (bodies!), `find_usages`, `get_diagnostics` serveren stil verouderde data terwijl de agent de code wijzigt | belangrijk | M |
| R3 | Workspace-load-diagnostics (WorkspaceFailed) worden verzameld maar alleen in de wiki getoond (eerste 3): bij een half-geladen solution geven alle andere tools stil onvolledige antwoorden | belangrijk | S |
| R4 | Harde load-failure (solution laadt niet, MSBuild ontbreekt) omzeilt het `{"error"}`-contract: de exception propagteert naar de SDK, de client krijgt een generieke melding, de echte oorzaak staat alleen in het temp-log. MSBuild-not-found — dé meest waarschijnlijke first-run-failure bij klanten — krijgt geen actionable vertaling | belangrijk | S |
| R5 | Cold start: `BuildAsync` compileert elk project (`GetCompilationAsync`); 500k+ regels ⇒ minuten wachttijd op de eerste tool-call, met risico op client-timeouts. Geen disk cache — elke herstart of `refresh_workspace` betaalt de volle prijs opnieuw | belangrijk | L (cache) / S (mitigatie) |
| R6 | Geheugen is grow-only: MSBuildWorkspace + Solution + alle Compilations + symbol-maps blijven voor de proceslevensduur vast; ad-hoc worktree-paden maken de cache onbegrensd. Geen LRU, geen idle-timeout | belangrijk | M |
| R7 | Cancellation-koppeling: het token van de **eerste** caller stuurt de gedeelde index-build (`WorkspaceProviderBase.cs:52-56`); agent A die annuleert breekt de build waar agent B op wacht | belangrijk | S |
| R8 | Log: hardcoded `%TEMP%\CodeIntelligenceMcp.log`, geen rotatie of size-cap (groeit eeuwig), exceptions zonder stack trace, meerdere serverinstanties delen één file | belangrijk | S |
| R9 | SSE-mode host een bewust nep-OAuth-endpoint zonder enige validatie (`Program.cs:105-157`) — nu localhost-only dev-gemak, maar levensgevaarlijk als een team dit "even" op een server zet | belangrijk | S (documenteren/afschermen) |
| R10 | Zelfde solution via naam én via absoluut pad ⇒ twee volledige onafhankelijke indexes (cache key = naam) | nice-to-have | S |
| R11 | Base branch default `"main"` zonder `origin/HEAD`-detectie; unborn HEAD geeft een NRE; fingerprint-berekening doet een volle `RetrieveStatus` per wiki-call | nice-to-have | S |

### Tests & docs

| # | Bevinding | Severity | Effort |
|---|---|---|---|
| Q1 | Alle analyzers achter de headline-features — ViolationDetector (21 regels!), ComplexityAnalyzer, RiskAnalyzer, CouplingAnalyzer, ChangeAnalyzer, ReferenceQueries, PatternScanner, WikiGenerator, dead code, hotspots — hebben **nul** tests. TASK.md's eigen Definition of Done (violation-detection-tests) is nooit gehaald | belangrijk | M–L |
| Q2 | Geen fixture-solution in de repo; de hele MSBuildWorkspace-laadroute (`RoslynLoader`) en de tools zelf worden alleen handmatig tegen privé-workspaces getest | belangrijk | M |
| Q3 | Tests die er zijn: 101/101 groen, strak AAA, xUnit + FluentAssertions — de parser-laag is degelijk gedekt | (positief) | — |
| Q4 | `docs/TOOLS.md` is stale: mist de complete JS- en Python-families plus 10 nieuwere analyse-tools; gebruikt privé-paden als voorbeeld | belangrijk | S |
| Q5 | README is verrassend goed (installatie, alle workspace-types, troubleshooting) maar mist: copy-example-config-stap, config-discovery-uitleg, `claude mcp add`-commando, `.mcp.json`-projectvoorbeeld, `cleanArchitecture`-semantiek, platform-statement | belangrijk | S |
| Q6 | Serialisatie-typo: `hassCmdletBinding` in `ps_find_function` (`PowerShellTools.cs:66`) | nice-to-have | S |

### Productization

| # | Bevinding | Severity | Effort |
|---|---|---|---|
| Z1 | Geen enkele telemetrie of usage-metriek (ook geen opt-in) — geen zicht op welke tools gebruikt worden of waar het misgaat bij testers | belangrijk | M |
| Z2 | Geen reproduceerbare token-benchmark: geen script, geen vaste taakset, geen meetmethode op JSONL-transcripts. De kernclaim is nu een anekdote | belangrijk | M |
| Z3 | Open-core-scheidslijn nog nergens technisch voorbereid (geen feature-flags, geen licentie-check-punt). De 21 violation-regels zijn hardcoded aan/uit — per-regel-configuratie is tegelijk een logische betaalde feature én een gemis voor gratis gebruikers met een andere architectuurstijl | belangrijk | M |

---

## 3. Quick wins (elk onder één avond, directe waarde)

1. **LICENSE + THIRD-PARTY-NOTICES** (P1) — kies een licentie voor de open-core-kern, voeg BSD-3-notice voor VBScript.Parser toe.
2. **Result caps op alle list-tools** (T1) — `maxResults`-parameter met default ~100 en een `truncated: true`-veld. Grootste kernbelofte-fix voor het minste werk.
3. **Compiler-gegenereerde symbols filteren** (T2) — `IsImplicitlyDeclared` / `MethodKind`-filter in `SearchSymbol`.
4. **Worktree-fix** (R1) — `Directory.Exists(gitPath) || File.Exists(gitPath)` in `ResolveRepoRoot`.
5. **First-run repareren** (P4) — csproj-Content optioneel maken (`Condition="Exists(...)"`), README-stap "copy `mcp-config.example.json` → `mcp-config.json`" vóór publish; example aanvullen met python/javascript-types.
6. **Config-discovery** (P5) — `--config <pad>`-CLI-arg + `CODEINTEL_CONFIG`-env-var, fallback naar BaseDirectory.
7. **Actionable load-errors** (R4) — try/catch rond `GetAsync` in de tool-laag: MSBuild-not-found → "installeer .NET SDK x / check global.json"; load-failure → eerste WorkspaceDiagnostics in de error-JSON.
8. **Unknown-workspace-error mét lijst + `list_workspaces`-tool** (C10).
9. **Wildcard-hint** (T4) — bij `*`/`?` in een query én nul hits: `{"hint":"search is substring-based; wildcards are matched literally"}` — of goedkoper: `*` gewoon als glob interpreteren.
10. **Relatieve paden in responses** (T3) — paden workspace-relatief maken; één plek (serialisatie-helper).
11. **Log-rotatie + stack traces** (R8) — size-cap met roll-over, `exception.ToString()` i.p.v. alleen message.
12. **Docs-scrub** (P7/Q4/Q5) — privé-paden uit getrackte docs, TOOLS.md aanvullen, README-gaten dichten, "Datalake2.Core" uit tool-descriptions.

## 4. v1.0-blockers (minimale set vóór een eerste externe tester)

| Blocker | Waarom dit de lat is |
|---|---|
| LICENSE + attributie + docs-scrub (P1, P7) | Zonder licentie mag niemand het draaien; privé-paden horen niet in een publiek pakket |
| First-run + config-discovery (P4, P5) | De tester strandt anders in de eerste 5 minuten — precies wat de audit-opdracht als toets stelt |
| Result caps + generated-symbol-filter (T1, T2) | De eerste demo-query mag de context van de tester niet opblazen; dit ís het product |
| Worktree-fix (R1) | De pitch noemt parallelle agents expliciet; de bug falsifieert de pitch |
| Actionable load-errors (R4) | MSBuild/SDK-mismatch is de meest waarschijnlijke failure op andermans machine; een generieke SDK-error is een support-ticket |
| Minimale packaging (P2, deel) | Op z'n minst een reproduceerbaar publish-artefact met versienummer; volledige `dotnet tool` + CI mag direct daarna |
| Fixture-solution smoke-test (Q2, minimaal) | Eén kleine .sln in `tests/fixtures/` + één integratietest die de volledige laadroute raakt — anders is elke refactor vóór de tester-release blind |

Geschat: ± 8–10 avonden. Bewust **niet** in deze set: analyzer-unit-tests (Q1, wel vóór betaalde release), staleness-verbreding (R2), disk cache (R5), cross-platform (P6 — eerste tester mag Windows zijn, maar zeg dat er dan bij).

## 5. Later (bewust geparkeerd, met reden)

- **Disk cache / incremental indexing** (R5/R6, L): grootste perf-win, maar pas bouwen als testers bevestigen dat cold start echt de pijn is — een warm-up-hint of achtergrond-preload kan al genoeg zijn.
- **Transitieve call hierarchy, rename-impact, derived types** (C1–C3): featuregroei; eerst valideren welke agent-vragen testers daadwerkelijk missen. Derived types (C3) is klein genoeg om naar voren te halen zodra iemand erom vraagt.
- **Multi-TFM en .slnf** (C5, C6): essentieel vóór brede verkoop, maar niet voor de eerste tester — wel expliciet als beperking documenteren.
- **Telemetrie + licentie-check + open-core-splitsing** (Z1, Z3): pas relevant als er externe gebruikers zíjn; te vroeg bouwen is verspilde architectuur.
- **Team/SSE-mode met echte auth** (R9): het nep-OAuth-endpoint is nu dev-gemak; een team-server is een aparte, latere productlijn. Tot die tijd: SSE-mode duidelijk als "local dev only" markeren.
- **Token-benchmark** (Z2): parkeren tot de caps (T1–T3) erin zitten — nu meten zou het product onnodig slecht laten scoren.

## 6. Voorstel per blocker / quick win

**LICENSE + scrub (P1, P7)** — Root-`LICENSE` (bijv. Apache-2.0 of BUSL voor open-core-kern; juridische keuze, geen technische), `THIRD-PARTY-NOTICES.md` met de BSD-3-tekst van kmvi, kopie van de notice in `src/VBScript.Parser/`. Scrub: `TASK.md`, `docs/TOOLS.md`, `CLAUDE.md`, `Tools/CodebaseWikiTool.cs:12`, `Tools/DiagnosticsTool.cs:16`. Valkuil: `vbscript-parser/` (de vendored upstream-map) apart houden of verwijderen — de attributie moet de gekopieerde code volgen, niet de map.

**Result caps (T1)** — Nieuwe optionele parameter `maxResults` (default 100) op `search_symbol`, `find_types`, `find_usages`, `find_violations`, `find_dead_code`, `asp_search`, `js_search`, `py_search`, `ps_search`; respons wordt `{ items: [...], total: n, truncated: bool }`. Files: `Tools/CSharpTools.cs`, `Tools/AspClassicTools.cs`, `Tools/JsTools.cs`, `Tools/PythonTools.cs`, `Tools/PowerShellTools.cs` + de betreffende index-methodes. Valkuilen: envelope-wijziging breekt bestaande response-shape (nu prima — er zijn nog geen externe gebruikers; daarna nooit meer gratis); `total` tellen vóór truncatie zodat de agent weet dat verfijnen zinvol is.

**Generated-symbol-filter (T2)** — In `RoslynWorkspaceIndex.SearchSymbol`: skip `symbol.IsImplicitlyDeclared`, `MethodKind.PropertyGet/PropertySet/EventAdd/EventRemove/Constructor` van records, en `EqualityContract`. Valkuil: expliciet gedeclareerde ctors wél tonen — filter op `IsImplicitlyDeclared` eerst, method-kinds alleen waar de property zelf al als hit verschijnt.

**Worktree-fix (R1)** — `GitDiffService.ResolveRepoRoot`: accepteer `.git` als file (LibGit2Sharp's `Repository.Discover` doet dit werk eigenlijk al — overweeg de handmatige walk volledig te vervangen). Valkuil: `Repository.Discover` geeft bij een worktree het pad naar de *gitdir*, niet de working directory; test beide vormen. Dit is meteen de plek om een integratietest met een echte worktree toe te voegen.

**First-run + config-discovery (P4, P5)** — csproj: `<Content Include="..\..\mcp-config.json" Condition="Exists('..\..\mcp-config.json')">`. `Program.cs`: pad-resolutie in volgorde CLI-arg `--config` → env-var `CODEINTEL_CONFIG` → `AppContext.BaseDirectory`. README: config-stap vóór publish-stap, example-file noemen. Valkuil: de bestaande `Mcp:Port`-configuratie loopt via het host-configsysteem; houd de mcp-config-resolutie daar los van, anders ontstaat verwarring over twee configbronnen.

**Actionable load-errors (R4)** — In `WorkspaceProviderBase.GetAsync` of een dunne wrapper in elke tool: catch → `{"error": "...", "detail": eerste 3 WorkspaceDiagnostics, "hint": ...}`. Specifiek: `MSBuildLocator`-failures mappen naar "geen compatibele .NET SDK gevonden; geïnstalleerd: [...]" (de locator kan instanties opsommen). Files: `Workspaces/WorkspaceProviderBase.cs`, `RoslynLoader.cs`, tool-classes. Valkuil: de failure-evictie (retry-gedrag) niet slopen — de catch moet ná de bestaande `TryRemove`-logica blijven werken.

**Minimale packaging (P2)** — Split de SSE-tak: entrypoint terug naar `Microsoft.NET.Sdk` met alleen stdio (SSE naar een apart project of achter een package-referentie), dan `PackAsTool` + `ToolCommandName` (bijv. `code-intel-mcp`) + `Version` in `Directory.Build.props`. Valkuil: `System.Management.Automation` en MSBuild-assemblies in een tool-package — test `dotnet tool install` op een schone machine expliciet; `ExcludeAssets=runtime` op Microsoft.Build.* is er al, dat moet zo blijven.

**Fixture-solution smoke-test (Q2)** — `tests/fixtures/SampleSolution/` met 3 kleine projecten (Core/Infrastructure/Web, een interface + implementatie + use case + bewuste violation), één `[Fact]` die `RoslynLoader.LoadAsync` + `get_type`/`find_implementations`/`find_violations` end-to-end draait. Valkuilen: MSBuildLocator in een testproces registreren kan maar één keer (collection fixture gebruiken); CI heeft de juiste SDK nodig — pin met `global.json` in de fixture.

**Overige quick wins** — C10: bekende workspace-namen in de error + `list_workspaces`-tool naast `refresh_workspace` in `WorkspaceManagementTool.cs`. T3: pad-relativering in één serialisatie-helper, workspace-root als basis. T4: glob-interpretatie via `Regex.Escape` + `.Replace("\\*", ".*")` in `SearchSymbol`. R8: size-check bij open + roll naar `.1`, `exception.ToString()` in `FileLoggerProvider.cs:50-52`. T5: `JavaScriptEncoder.UnsafeRelaxedJsonEscaping` in de gedeelde `JsonSerializerOptions` (output gaat naar een LLM, niet naar HTML — veilig). Q6: typo `hassCmdletBinding` fixen zolang er nog geen externe gebruikers zijn.

---

## Bundel-notities (SQL Schema MCP)

- **Gedeelde install-story is de echte synergie:** beide zijn .NET stdio-MCP's voor hetzelfde publiek. Eén `dotnet tool install`-patroon, één config-formaat (`workspaces` hier, `connections` daar — zelfde JSON-skelet, zelfde discovery-volgorde CLI-arg/env-var/naast-binary), één CI-publish-pipeline. Alles wat voor P2/P3/P5 gebouwd wordt, moet herbruikbaar zijn — overweeg een gedeelde `McpHost`-bibliotheek vóór de tweede repo dezelfde problemen oplost.
- **Gezamenlijke pitch schrijft zichzelf:** "je agent ziet je code én je database" — `find_usages` van een kolomnaam in C# + `sql_find_column` in ASP + `get_table_schema` uit de SQL-MCP is een demo die geen van beide alleen kan.
- **Zelfde open-core-vragen, één keer beantwoorden:** licentie-keuze, telemetrie-opt-in en licentie-check-mechanisme moeten identiek zijn, anders ontstaan twee half-verschillende producten.
- **Let op bij bundeling in één proces:** de SQL-MCP heeft credentials/connection strings; de code-MCP is read-only op de filesystem. Ander risicoprofiel — een gecombineerde server erft het zwaarste profiel. Aparte processen, gedeelde tooling.
- Beslissing over bundelen valt pas na de audit van de SQL-repo; deze notities zijn input, geen conclusie.

---

## Wrap-up

**Beslissingen (voorgesteld):** result caps + envelope-wijziging nu doorvoeren zolang er geen externe gebruikers zijn; SSE-tak uit het entrypoint-project t.b.v. `dotnet tool`; eerste externe tester mag Windows-only zijn, mits expliciet vermeld.

**Open vragen:** licentiekeuze voor de open-core-kern (Apache-2.0 vs BUSL vs eigen EULA — juridisch, niet technisch); of de 21 violation-regels configureerbaar worden vóór of ná de eerste tester; naam van het `dotnet tool`-commando.

**Startpunt volgende sessie:** quick wins 1–5 uit sectie 3 (LICENSE, result caps, generated-symbol-filter, worktree-fix, first-run) — samen ± 2 avonden en het halve blocker-lijstje; daarna de packaging-split (P2).
