# Taak 21 — Documentatie-waarheidspas (ALS LAATSTE)

**Status:** open. **Plansectie:** "Task 21". **Audit-bevindingen:** P7/P8/Q4/Q5 —
stale docs, privé-paden in getrackte files, README-gaten, verkeerde versies in
CLAUDE.md.

Pas uitvoeren als taken 6-20 af zijn: de docs moeten het EINDgedrag beschrijven.

## Wijzigingen

### README.md
- Copy-example-config-stap ("kopieer `mcp-config.example.json` → `mcp-config.json`")
  VÓÓR de publish-stap (de csproj-Content is na taak 10 conditioneel, maar de
  server heeft de config alsnog nodig).
- `--config`-CLI-arg en `CODEINTEL_CONFIG`-env-var documenteren (taak 10).
- `claude mcp add`-commando + project-`.mcp.json`-voorbeeld toevoegen.
- Platform-statement: getest op Windows; macOS/Linux via `dotnet publish -r <rid>`,
  MSBuildLocator vereist de .NET SDK.
- `cleanArchitecture`-semantiek: stuurt de layer-rules; auto-detectie op
  `*.Core`/`*.Infrastructure`-naamgeving als hij ontbreekt.
- SSE-mode expliciet markeren als local-dev-only (nep-OAuth zonder validatie).
- Nieuwe features benoemen: wildcards, maxResults/envelope, `list_workspaces`,
  `find_derived_types`, `depth` op find_callers, `.slnf`, relatieve paden,
  stale-vlag.

### docs/TOOLS.md — volledig regenereren uit de bron
Grep alle `[McpServerTool(Name = ...)]` + `[Description(...)]` en schrijf de
tabel vanuit de code (geen handmatig overschrijven van oude tekst). Dekt dan
automatisch: alle ~50 tools, nieuwe parameters, de `OkList`-envelope
(documenteer `{ total, returned, truncated, stale?, hint?, items }` één keer
centraal), wildcard-syntax. Vervang privé-voorbeelden (`datalake2`,
`C:/Git/Datalake2-feature/...`) door `myapp`/`C:/path/to/...`.

### Tool-descriptions in code
`Tools/CodebaseWikiTool.cs` (focusArea-description) en `Tools/DiagnosticsTool.cs`
(project-description) noemen "Datalake2.Core" als voorbeeld → `MyApp.Core`.

### CLAUDE.md (repo-root)
- Versies corrigeren: ModelContextProtocol 1.1.0, Roslyn 5.3.0 (staat er 1.2.0/4.13.0).
- Nieuwe conventies documenteren: ToolResponses, WorkspaceAccess, Rel(),
  matcher-duplicatie (verwijs naar docs/handoff/CONVENTIONS.md of neem het over).
- De privé-workspace-tabel (datalake2/datalake1-paden) vervangen door een
  verwijzing naar `mcp-config.example.json`.

### mcp-config.example.json
`python`- en `javascript`-workspacevoorbeelden toevoegen (ontbreken).

### Scrub getrackte docs
`TASK.md` en `docs/TOOLS.md` bevatten `C:/Git/...`-privépaden. TASK.md is
historisch — overweeg verplaatsen naar `docs/history/` met een scrub, of vraag
de gebruiker of het weg mag. NIET stilletjes verwijderen.

## Acceptatie

- Een vreemde .NET-dev kan van clone tot eerste succesvolle tool-call komen
  met alleen de README.
- Geen `C:/Git/`-privépaden meer in getrackte files (behalve waar de gebruiker
  anders beslist).
- Commit: `docs: truthful README, regenerated TOOLS.md, scrubbed examples`
