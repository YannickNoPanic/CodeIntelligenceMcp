# Product Audit — Code Intelligence MCP

> **Gebruik:** open Claude Code in de repo van de Code Intelligence MCP en geef dit document als opdracht, of zet het in `.claude/commands/` als `/product-audit`. Doorloop de fases in volgorde. Maak tijdens de audit géén code-wijzigingen.

## Context

Deze MCP-server is gebouwd als interne tooling voor mijn eigen Claude Code-workflow op Datalake2 (.NET 10, grote codebase). Roslyn/MSBuildWorkspace-gebaseerd; vervangt `Search + Read × N`-patronen door gerichte calls zoals `get_symbol`, `find_implementations` en `find_references`, met als kernbelofte: fors minder tokens per sessie.

Doel van deze audit: bepalen wat er nodig is om hiervan een **verkoopbaar product** te maken voor .NET-teams die AI-agents (Claude Code, Copilot) op grote codebases gebruiken. Beoogd model: open-core — gratis voor individuele devs, betaald voor teams (per-seat). Distributie als `dotnet tool`.

Jouw rol: kritische externe reviewer die beslist of dit product-klaar is. Niet complimenteus zijn — gaten vinden.

## Fase 1 — Inventarisatie (alleen lezen, geen oordeel)

1. **Toolset**: elke MCP-tool met naam, parameters, response-vorm, en geschatte token-kosten van een typische response.
2. **Architectuur**: projectstructuur, hosting-model (stdio via `Host.CreateApplicationBuilder`?), dependencies, target framework(s), hoe MSBuildWorkspace wordt geladen en gecachet.
3. **Configuratie**: wat is hardcoded vs. configureerbaar? Zoek expliciet naar Datalake2- of machine-specifieke aannames: solution-paden, projectnamen, MSBuild-locaties, SDK-versies.
4. **Tests**: dekking per tool; zijn er tests tegen een echte (fixture-)solution? Huisstijl: xUnit + NSubstitute + FluentAssertions.
5. **Docs**: README, installatie-instructies, voorbeeld-`.mcp.json`, troubleshooting.

## Fase 2 — Gap-analyse

Beoordeel per bevinding met severity `blocker` (kan zo niet verkocht worden), `belangrijk` (eerste betalende klant loopt hier direct tegenaan) of `nice-to-have`, plus effort (S/M/L).

### 2a. Tool-coverage & Roslyn-diepgang
- Welke veelvoorkomende agent-vragen kan de server nog niet beantwoorden? Check minimaal: call hierarchy (wie roept X aan, transitief), impact-analyse van een rename, dependency graph tussen projecten, symbol search met wildcards/fuzzy, type hierarchy (base/derived), attribuut-gebaseerd zoeken (bijv. alle `IUseCase<TIn,TOut>`-implementaties).
- Randgevallen van moderne C#: partial classes, source generators (worden gegenereerde symbols gezien?), meerdere target frameworks per project, solution filters (`.slnf`), file-scoped namespaces, global usings.
- Response-compactheid: elke overbodige token ondermijnt de kernbelofte. Zijn responses gestructureerd en minimaal, of komen er hele declaraties/bodies mee terug waar een signature volstaat?

### 2b. Robuustheid & performance
- Gedrag bij: corrupte of niet-ladende solution, mislukte NuGet restore, ontbrekende SDK/workloads, extreem grote solution (500k+ regels), concurrent requests vanuit parallelle agents (git worktrees!).
- Foutmeldingen: krijgt de agent korte, bruikbare errors of stack traces?
- Geheugengebruik en levensduur: wat gebeurt er bij lange sessies? Is er een invalidation/reload-strategie als de agent zelf code wijzigt tijdens de sessie — en zo niet, hoe stale wordt de workspace dan?
- Koude start: hoe lang duurt de eerste load van een grote solution, en is dat acceptabel of moet er een warm-up/cache komen?

### 2c. Installatie & DX (de eerste 5 minuten)
- Simuleer het pad van een vreemde .NET-dev: `dotnet tool install -g` → `claude mcp add` / `.mcp.json` → eerste succesvolle call. Waar strandt dat nu?
- Wat ontbreekt voor `dotnet tool`-packaging: NuGet-metadata, versioning-strategie, CI-publish pipeline (Azure DevOps)?
- Werkt het op een schone machine zonder mijn setup — Windows, macOS, Linux, devcontainer? Welke MSBuild/SDK-detectie is nodig?

### 2d. Productization
- Waar zou de open-core scheidslijn technisch moeten liggen (bijv. gratis: single solution + kern-tools; betaald: multi-repo, gedeelde team-config, CI-integratie, prioriteit-support)? Waar kan een licentie-check zitten zonder de gratis versie te verpesten?
- Opt-in telemetrie/diagnostics: kan ik straks zien welke tools gebruikt worden en waar het misgaat?
- Wat ontbreekt om de token-benchmark (before/after op Datalake2) **reproduceerbaar** te maken als marketing-materiaal: benchmark-script, vaste taakset, meetmethode op JSONL-transcripts?

## Fase 3 — Output

Schrijf een rapport naar `docs/product-audit-<datum>.md` met:

1. **Samenvatting** (max. 10 regels): afstand tot verkoopbaar, uitgedrukt in avonden werk.
2. **Bevindingen-tabel**: categorie, bevinding, severity, effort.
3. **Quick wins**: alles onder één avond met directe waarde.
4. **v1.0-blockers**: de minimale set vóór een eerste externe tester.
5. **Later**: bewust geparkeerd, met reden.
6. **Voorstel per blocker/quick win**: aanpak, betrokken files, valkuilen — maar voer niets uit.

## Werkafspraken voor deze sessie

- Lees gericht; gebruik waar mogelijk de MCP-tools van de repo zelf in plaats van hele files te dumpen.
- Maximaal 3 verduidelijkende vragen vooraf; daarna aannames maken en expliciet noteren in het rapport.
- Sluit af met een `/wrap-up`-waardige samenvatting: beslissingen, open vragen, startpunt volgende sessie.
- Noteer bevindingen die relevant zijn voor een eventuele bundel met de SQL Schema MCP (gedeelde config, één install, gezamenlijke pitch) in een aparte sectie "Bundel-notities" — de beslissing zelf valt pas na de audit van de andere repo.
