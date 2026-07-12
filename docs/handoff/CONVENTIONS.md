# CONVENTIONS — verplichte patronen en valkuilen

Lees dit vóór je code aanraakt. Deze conventies zijn tijdens taak 1-5
vastgelegd; afwijken breekt consistentie of tests.

## Response-pipeline (ToolResponses)

Alle tool-output loopt via `src/CodeIntelligenceMcp/Tools/ToolResponses.cs`:

```csharp
ToolResponses.Ok(object result, bool stale = false)
ToolResponses.OkList<T>(IReadOnlyList<T> items, int maxResults = 100, bool stale = false, string? hint = null)
ToolResponses.Err(string message, string? hint = null, IReadOnlyList<string>? detail = null)
ToolResponses.JsonOptions   // camelCase + UnsafeRelaxedJsonEscaping + WhenWritingNull
```

- `OkList`-envelope: `{ total, returned, truncated, stale?, hint?, items }`.
  `maxResults <= 0` = onbeperkt. Null-velden worden weggelaten.
- `Ok(x, stale: true)` wikkelt als `{ stale: true, hint: "...", result: x }`.
- NOOIT een eigen `JsonSerializerOptions` of tweede envelope-stijl introduceren.
- Wiki-tools retourneren markdown (geen JSON) — dat blijft zo.

## Workspace-toegang (WorkspaceAccess)

Elke tool begint met:

```csharp
(RoslynWorkspaceIndex? index, string? error) = await WorkspaceAccess.GetAsync(roslynProvider, config, "dotnet", workspace, ct);
if (index is null)
    return error!;
```

Workspace-typestrings: `"dotnet"`, `"asp-classic"`, `"powershell"`, `"python"`,
`"javascript"`. Elke tool-class krijgt `McpConfig config` via de primary
constructor (DI-singleton).

Load-fouten uit de indexers gooien `WorkspaceLoadException(message, hint, detail)`
(gedefinieerd in `src/CodeIntelligenceMcp.Roslyn/WorkspaceLoadException.cs`) —
WorkspaceAccess vertaalt die naar nette error-JSON. `OperationCanceledException`
altijd doorgooien, nooit inslikken.

## Paden in responses

- Response-modellen krijgen solution-relatieve forward-slash-paden via
  `RoslynWorkspaceIndex.Rel(path)` (internal method).
- Interne opslag (`IndexedType.FilePath`, analyzers die van disk lezen) blijft
  ABSOLUUT. Relativiseer alleen op het moment dat een waarde een response-model
  in gaat.
- ViolationDetector: rules leveren absolute paden; `DetectAsync` relativiseert
  centraal. Nieuwe rules dus niets aan paden doen.
- Paden buiten de solution-root blijven absoluut (Rel geeft ze onveranderd terug).

## Naam-matching

- Roslyn-kant: `SymbolQueryMatcher` (`src/CodeIntelligenceMcp.Roslyn/`).
- File-walk-kant: `NameMatcher` (`src/CodeIntelligenceMcp.Common/`).
- Zelfde semantiek: `*`/`?` = glob op de naam, anders case-insensitive substring.
- Dit is BEWUSTE duplicatie: `.Roslyn` mag niet van `.Common` afhangen.
  Wijzig je de semantiek, wijzig dan beide + beide testfiles.

## Search-filtering (Roslyn)

In `SearchSymbol` worden overgeslagen: `member.IsImplicitlyDeclared`,
method-kinds buiten `Ordinary`/`Constructor`/`LocalFunction`, en de
record-property `EqualityContract`. Wildcard-queries matchen op `Symbol.Name`;
substring-queries op de FQN (historisch gedrag).

## Tests

- xUnit + FluentAssertions. NSubstitute wordt VERWIJDERD (taak 17) — niet
  gebruiken; fake providers zijn kleine inline classes (zie
  `WorkspaceAccessTests.cs`).
- AAA met witregels, naamgeving `Method_Scenario_ExpectedResult`.
- In-memory index bouwen: `RoslynWorkspaceIndex.CreateForTesting(compilations,
  cleanArch, rootDir?)` — zie `BuildIndex`/`BuildIndexWithRoot`-helpers in
  `RoslynLookupTests.cs`. Beperking: geen `Solution`, dus geen
  SymbolFinder-gebaseerde queries (FindUsages/FindCallers) — die kunnen alleen
  via de fixture-solution van taak 18.
- `InternalsVisibleTo` voor de testassembly bestaat op `.Roslyn` én het
  serverproject.

## Tooling-valkuilen (Windows/PowerShell 5.1)

- **DLL-locks:** live MCP-servers locken build-output. Nooit processen killen;
  bouw/test met `-o <tempdir>` als het misgaat.
- **Encoding:** bronbestanden zijn UTF-8 zonder BOM. PowerShells `Set-Content`
  vernielt em-dashes (mojibake) en voegt BOMs toe. Gebruik bij scripted edits:
  ```powershell
  [System.IO.File]::WriteAllText($path, $text, (New-Object System.Text.UTF8Encoding($false)))
  ```
  en controleer daarna op `â`-tekens in de diff.
- CRLF-line-endings, 4 spaties indent, laatste regel newline (.editorconfig).

## Commits

Conventional commits, één taak per commit, body legt de "waarom" uit.
Afsluiten met `Co-Authored-By: Claude <model> <noreply@anthropic.com>`.
Nooit `--no-verify`. De untracked `PublishProfiles/`-map van de gebruiker
niet meecommitten — dus geen blind `git add -A` zonder status-check.
