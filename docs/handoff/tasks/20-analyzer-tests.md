# Taak 20 — Analyzer-tests: Complexity, Coupling, Risk, PatternScanner

**Status:** AFGEROND (zie STATE.md). **Plansectie:** "Task 20". **Audit-bevinding:** Q1 — alle
analyzers achter de headline-features zijn ongetest.

## Aan te maken

- `tests/CodeIntelligenceMcp.Tests/ComplexityAnalyzerTests.cs`
- `tests/CodeIntelligenceMcp.Tests/CouplingAnalyzerTests.cs`
- `tests/CodeIntelligenceMcp.Tests/RiskAnalyzerTests.cs`
- `tests/CodeIntelligenceMcp.Tests/PatternScannerTests.cs`

Gebruik de `TestIndex`-helper uit taak 19 (of maak hem hier als taak 19 nog
niet gedaan is — zie die file).

## Minimale asserties (lees elke analyzer eerst!)

- **Complexity:** methode met 3 `if`s + 1 loop scoort 5±1; straight-line
  methode scoort 1; `minComplexity` filtert.
  LET OP: `ComplexityAnalyzer.ComputeAllAsync` loopt via
  `index.GetAllDocuments` — check of dat met `CreateForTesting` werkt (geen
  Solution!). Zo niet: test `ComputeComplexity` indirect via de fixture (taak
  18) of maak de complexity-kern testbaar op een los syntax-body. Documenteer
  de keuze in de fileheader.
- **Coupling:** type dat 6 verschillende externe types raakt rapporteert
  coupling >= 6; `minCoupling` filtert. (`CouplingAnalyzer.GetCoupling` werkt
  op `_allTypes`/symbols — zou in-memory moeten werken.)
- **Risk:** type met hoge coupling en ZONDER `FooTests`-klasse scoort hoger dan
  hetzelfde type MET een `FooTests`-type in een project met naam eindigend op
  `.Tests` (zo detecteert `TestClassNames` ze).
- **PatternScanner:** telt use cases (`*UseCase`-naamconventie) en interfaces
  correct op een compilatie met 4 types.

## Acceptatie

- Nieuwe + bestaande tests groen.
- Commit: `test: analyzer coverage for complexity, coupling, risk, patterns`
