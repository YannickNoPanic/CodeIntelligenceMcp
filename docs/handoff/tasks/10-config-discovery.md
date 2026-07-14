# Taak 10 — Config discovery + first-run reparatie

**Status:** AFGEROND (zie STATE.md). **Plansectie:** "Task 10". **Audit-bevindingen:** P4 (blocker,
first-run op schone clone faalt: `mcp-config.json` is gitignored maar de csproj
eist hem als `<Content>`) en P5 (blocker, config alleen laadbaar naast de exe).

## Bestanden

- `src/CodeIntelligenceMcp/Program.cs` (padresolutie, missing-config-tolerantie)
- `src/CodeIntelligenceMcp/Config/McpConfigLoader.cs` (vriendelijkere JSON-fouten)
- `src/CodeIntelligenceMcp/CodeIntelligenceMcp.csproj` (conditionele Content-include)
- Nieuw: `tests/CodeIntelligenceMcp.Tests/McpConfigLoaderTests.cs`

## Gewenst gedrag

- Resolutievolgorde: CLI-arg `--config <pad>` → env-var `CODEINTEL_CONFIG` →
  `AppContext.BaseDirectory/mcp-config.json`.
- **Missing config is niet meer fataal**: warning loggen, doorgaan met lege
  workspace-lijst (ad-hoc absolute paden werken op elke dotnet-tool).
  Let op: `McpConfigLoader.Load` gooit nu `FileNotFoundException` (regel ~14);
  dat wordt: return `new McpConfig()` (Workspaces is al default `[]`).
- Malformed JSON blijft fataal, maar wrap `JsonException` in
  `InvalidOperationException($"mcp-config.json is invalid JSON: {ex.Message} (path: {path})")`.

## Implementatie

1. Tests eerst (volledige code in plansectie Task 10; `NullLogger.Instance`
   vereist `Microsoft.Extensions.Logging.Abstractions`-using; loader is
   `internal` — InternalsVisibleTo op het serverproject bestaat al).
2. Loader-wijzigingen.
3. `Program.cs`: local function `ResolveConfigPath(args)` zoals in het plan;
   vervang de hardcoded `Path.Combine(AppContext.BaseDirectory, "mcp-config.json")`
   (regel ~27). NB: houd dit gescheiden van het host-configsysteem (`Mcp:Port`).
4. csproj: `Condition="Exists('..\..\mcp-config.json')"` op het Content-item
   (regel ~22-26).
5. Clean-clone-simulatie: hernoem tijdelijk repo-root `mcp-config.json`,
   `dotnet build src/CodeIntelligenceMcp -o <tempdir>\cleanbuild`, hernoem terug
   (OOK bij failure terugzetten — het bestand is gitignored en dus onvervangbaar!).

## Acceptatie

- Nieuwe + bestaande tests groen; clean-clone-build slaagt.
- Commit: `feat: config via --config/CODEINTEL_CONFIG, non-fatal missing config, clean-clone build`
