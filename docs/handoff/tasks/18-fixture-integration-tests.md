# Taak 18 — Fixture-solution + end-to-end integratietests

**Status:** AFGEROND (zie STATE.md). **Plansectie:** "Task 18" (bevat volledige testcode).
**Audit-bevinding:** Q2 — geen fixture-.sln in de repo; de hele
MSBuildWorkspace-laadroute en de tools worden alleen handmatig tegen
privé-workspaces getest. Elke refactor is blind.

## Aan te maken

```
tests/fixtures/FixtureSolution/
  FixtureSolution.sln
  Fixture.Core/Fixture.Core.csproj            (net10.0, geen deps)
    IGreetUseCase.cs    public interface IGreetUseCase { string Greet(string name); }
    GreetUseCase.cs     implementeert IGreetUseCase
    HttpViolation.cs    private readonly System.Net.Http.HttpClient _client = new();
                        (bewust aas voor core-no-http + direct-instantiation)
  Fixture.Infrastructure/Fixture.Infrastructure.csproj  (ref Core)
    GreetRepository.cs
  Fixture.Web/Fixture.Web.csproj              (ref Core + Infrastructure)
    Caller.cs           methode die GreetUseCase.Greet aanroept
tests/CodeIntelligenceMcp.Tests/Integration/
  MsBuildFixture.cs     collection fixture, één gedeelde index-build
  RoslynIntegrationTests.cs
```

NIET toevoegen aan `CodeIntelligenceMcp.slnx` — de fixture wordt alleen via
MSBuildWorkspace geladen tijdens de tests. Check dat `.gitignore` de
`bin`/`obj` van de fixture dekt (repo-brede patronen dekken dit vermoedelijk al).

## Kernpunten

- `MsBuildFixture`: `[CollectionDefinition("msbuild")]`; roept
  `RoslynLoader.RegisterMSBuild()` aan (idempotent, lock-guarded — hergebruiken,
  niet dupliceren) en cachet één `Task<RoslynWorkspaceIndex>` via
  `RoslynLoader.LoadAsync(slnPath, new CleanArchitectureNames("Fixture.Core", "Fixture.Infrastructure", "Fixture.Web"))`.
- Fixture-pad resolven: wandel omhoog vanaf `AppContext.BaseDirectory` tot een
  dir met `tests/fixtures/FixtureSolution` gevonden is.
- Vier tests uit de plansectie: TypeCount/GetType, FindImplementations,
  FindCallers (Caller → GreetUseCase.Greet), core-no-http-violation.
- **Extra t.o.v. plan** (afhankelijk van of taken 6/12 al af zijn):
  - depth=2-test voor transitieve callers (taak 12): voeg `Outer.cs` toe in
    Fixture.Web met een methode die `Caller` aanroept; assert `Depth == 2`.
  - staleness-test (taak 6): fixture is geen git-repo, dus `IsStaleCached()`
    hoort false te blijven — assert dat.
  - Let op: paden in results zijn workspace-RELATIEF (taak 5) — assert dus op
    bijv. `"Fixture.Core/HttpViolation.cs"` met forward slashes, niet absoluut.

## Valkuilen

- Eerst standalone bouwen: `dotnet build tests/fixtures/FixtureSolution -o <tempdir>\fixturebuild`.
- MSBuildLocator in de testhost kan botsen met al geladen MSBuild-assemblies;
  de tests draaien al serieel in hun eigen collection. Diagnoseer op de echte
  fout, niet op verwachting.
- Cold start van de fixture-load moet < 30 s zijn (3 mini-projecten).
- Geen `global.json` toevoegen zonder na te denken: dat pint de SDK repo-breed.

## Acceptatie

- Integratietests + alle unit-tests groen.
- Commit: `test: fixture solution with end-to-end Roslyn integration tests`
