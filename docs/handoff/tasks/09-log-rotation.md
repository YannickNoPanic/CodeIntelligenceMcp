# Taak 9 — Log-rotatie + volledige exception-detail

**Status:** open. **Plansectie:** "Task 9". **Audit-bevinding:** R8 — het log
(`%TEMP%\CodeIntelligenceMcp.log`) groeit eeuwig (geen rotatie/cap), exceptions
loggen alleen type+message zonder stack trace, en de timestamp gebruikt
impliciete culture.

## Bestanden

- `src/CodeIntelligenceMcp/Logging/FileLoggerProvider.cs`
- Nieuw: `tests/CodeIntelligenceMcp.Tests/FileLoggerProviderTests.cs`

## Implementatie (TDD — volledige testcode in plansectie Task 9)

1. **Rotatie:** in de constructor, vóór het openen van de stream: als het
   bestand bestaat en > 10 MB is, `File.Move(path, path + ".old", overwrite: true)`.
2. **Stack traces:** in de log-methode de exception-regel vervangen door
   `exception.ToString()` (type + message + stack) i.p.v. alleen message.
3. **Culture:** timestamp via
   `DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)`.

## Tests

- `Ctor_ExistingFileOverSizeCap_RollsToOld`: schrijf 11 MB dummy, construeer
  provider, assert `.old` bestaat en het nieuwe log klein is.
- `Log_WithException_WritesStackTrace`: gooi+vang een exception, log met
  `LogError(ex, "failed")`, assert de logtekst "boom" én een "at "-frame bevat.
- Provider disposen vóór het lezen van het bestand (file lock).

## Let op

- De provider wordt in `Program.cs` regel ~10 al vóór DI aangemaakt en bij
  falen exit 1 — dat gedrag niet wijzigen.
- Meerdere serverinstanties delen het bestand via `FileShare.ReadWrite`;
  rotatie kan botsen met een tweede live instantie. Acceptabel: vang
  `IOException` rond de `File.Move` en ga door zonder rotatie.

## Acceptatie

- Nieuwe + bestaande tests groen.
- Commit: `fix: log rotation at 10MB and full exception stack traces`
