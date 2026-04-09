# Changes from upstream

Source copied from `https://github.com/YannickNoPanic/vbscript-parser` (which itself forks `https://github.com/kmvi/vbscript-parser`).

## Retargeted to net10.0

- `TargetFramework` changed from `netstandard2.0` to `net10.0`
- Removed NuGet packaging metadata (not published as a package)

## Fixed ambiguous Range reference (CS0104)

`VBScriptParser.cs` lines 2004 and 2051: `new Range(...)` is ambiguous between
`VBScript.Parser.Ast.Range` and `System.Range` (added in .NET 5). Fixed by
qualifying as `new Ast.Range(...)`.

## Fixed nullable warnings (net10.0 nullable enabled)

- `Ast/Range.cs`, `Ast/Position.cs`, `Ast/Location.cs`: `Equals(object obj)` → `Equals(object? obj)` (CS8765)
- `Extensions.cs`: `ResourceManager.GetString()` returns `string?`; added `?? string.Empty` and explicit `CultureInfo.InvariantCulture` (CS8603)
- `VBScriptParser.cs` line 1879: `token.ToString()` returns `string?`; added null-forgiving `!` (CS8603)

## Removed obsolete serialization constructor (SYSLIB0051)

`VBSyntaxErrorException.cs`: removed `[Serializable]`, `using System.Runtime.Serialization`,
and the `protected VBSyntaxErrorException(SerializationInfo, StreamingContext)` constructor.
Binary formatter serialization is not supported on .NET 7+.
