# Taak 19 — ViolationDetector-ruledekking

**Status:** AFGEROND (zie STATE.md). **Plansectie:** "Task 19". **Audit-bevinding:** Q1 — de 21
violation-rules (het paradepaardje) hebben nul tests; TASK.md's eigen
Definition of Done werd nooit gehaald.

## Aan te maken

- `tests/CodeIntelligenceMcp.Tests/ViolationDetectorTests.cs`
- Een gedeelde `TestIndex`-helper (statisch klasje in de testproject-root) die
  `RoslynWorkspaceIndex.CreateForTesting` wrapt met `(projectName, source)`-tuples
  — extraheer/deel de bestaande `BuildIndex`-logica uit `RoslynLookupTests.cs`
  in plaats van een derde kopie te maken. Const:
  `TestIndex.CleanArch = new CleanArchitectureNames("App.Core", "App.Infrastructure", "App.Web")`.

## Te dekken rules (minimaal, één gerichte test per rule)

`core-no-http`, `missing-cancellation-token`, `no-async-void`, `empty-catch`,
`throw-ex`, `too-many-params`, `dto-in-core`, `usecase-not-sealed`.

Voorbeeldvorm (AAA, zie plansectie voor de volledige voorbeeldtest):

```csharp
[Fact]
public async Task DetectAsync_EmptyCatch_FlagsMethod()
{
    RoslynWorkspaceIndex index = TestIndex.Create(("App.Core",
        "public class Svc { public void Run() { try { } catch { } } }"));
    var detector = new ViolationDetector(index, TestIndex.CleanArch);

    var violations = await detector.DetectAsync("empty-catch", CancellationToken.None);

    violations.Should().ContainSingle();
}
```

## Belangrijk

- **Lees eerst `ViolationDetector.cs` per rule** — semantiek niet raden. Fix
  testmisverstanden door de detector te lezen, NIET door asserties af te zwakken.
- Rules die `Solution`/documents nodig hebben (bijv. rules die via
  `GetAllDocuments` lopen zoals `empty-catch`/`throw-ex`/`async-over-sync` —
  verifieer dit!) werken mogelijk NIET met `CreateForTesting` (geen Solution).
  Verplaats zulke rule-tests naar de integratie-fixture (taak 18) en noteer dat
  in de fileheader. Check per rule of hij `_allTypes` (werkt) of
  `GetAllDocuments`/`Solution` (werkt niet in-memory) gebruikt.
- `DetectAsync` relativiseert FilePath centraal (taak 5) — assert dus geen
  absolute paden.

## Acceptatie

- 8+ nieuwe tests groen, bestaande groen.
- Commit: `test: ViolationDetector rule coverage`
