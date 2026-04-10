# DEPENDENCIES.md — Package Evaluation for Python & JavaScript Analyzers

## Summary

| Language | Package | Decision |
|---|---|---|
| Python parser | None | Custom regex — zero deps |
| Python TOML | `Tomlyn` | Use — proper TOML v1.0 parser, pure .NET |
| JavaScript parser | None | Custom regex — zero deps |
| JavaScript JSON | `System.Text.Json` | Already in framework |

---

## Python

### Option 1: Python.NET (pythonnet)

**NuGet**: `pythonnet` (Python.Runtime)
**Version**: 3.0.x
**License**: MIT

| Criterion | Assessment |
|---|---|
| External runtime required | Yes — Python 3.x must be installed on the host machine |
| Platform | Windows/Linux/Mac, but PATH/PYTHONHOME setup is fragile |
| Startup cost | ~500ms+ (Python interpreter initialization) |
| Accuracy | 100% (uses Python's own `ast` module) |
| Maintenance | Active but complex interop layer |
| Docker | Requires Python in the image |

**Verdict: Rejected.** The MCP server is a background process that must start cleanly on any
developer machine. Requiring a Python runtime installation is an unacceptable dependency for
a tooling utility. Path setup failures produce cryptic errors that are hard to diagnose.

---

### Option 2: IronPython

**NuGet**: `IronPython` 3.4.x
**License**: Apache 2.0

| Criterion | Assessment |
|---|---|
| External runtime required | No — pure .NET |
| Python version | Python 3.4 subset (not full Python 3.12) |
| AST access | Requires running Python code inside IronPython to call `ast.parse()` — interpreter overhead |
| Startup cost | ~200-300ms (interpreter setup) |
| Accuracy | High for Python 2-3.4 syntax, gaps for newer features (match/case, walrus, etc.) |
| Maintenance | Active but lags Python releases |

**Verdict: Rejected.** IronPython is an interpreter, not a parsing library. Getting AST data
requires running code inside the IronPython runtime, which is heavyweight for static analysis.
It also cannot parse Python 3.10+ syntax (structural pattern matching, etc.).

---

### Option 3: TreeSitter .NET Bindings

**NuGet**: Various (no single authoritative binding)
**License**: MIT (tree-sitter itself)

| Criterion | Assessment |
|---|---|
| Native deps | Yes — native `libtree-sitter` per platform |
| .NET binding maturity | Low — no official package, multiple competing community packages |
| Accuracy | High |
| Maintenance | Community-maintained bindings |

**Verdict: Rejected.** Native per-platform binaries add deployment complexity. No authoritative
.NET binding exists. The maturity and maintenance risk is too high.

---

### Option 4: Custom Regex Parser (Recommended)

**NuGet**: None

| Criterion | Assessment |
|---|---|
| External deps | None |
| Python version | Any (regex targets syntax elements that have not changed since Python 3.0) |
| Accuracy | 85-90% for structural extraction (functions, classes, imports) |
| Startup cost | <10ms |
| Maintenance | In-repo |

**Verdict: Recommended.** See `PLAN-PYTHON.md` for full rationale.

---

### Tomlyn (for pyproject.toml)

**NuGet**: `Tomlyn`
**Version**: 0.17.x (latest stable)
**License**: BSD 2-Clause
**Size**: ~80KB

| Criterion | Assessment |
|---|---|
| TOML version | 1.0 compliant |
| External runtime | None — pure .NET |
| Cross-platform | Yes |
| Maintenance | Active (used by .NET SDK tooling) |
| Alternatives | Manual regex — rejected due to TOML nested table complexity |

**Verdict: Use Tomlyn.** `pyproject.toml` uses TOML's nested table feature
(`[project.optional-dependencies]`, `[tool.poetry.dependencies]`). Regex cannot reliably
handle this. The cost (one small NuGet package) is clearly justified.

Requirements.txt and setup.cfg use simple line-oriented formats that regex handles fine.
Only pyproject.toml needs Tomlyn.

---

## JavaScript / TypeScript

### Option 1: Esprima.NET

**NuGet**: `Esprima`
**Version**: 3.0.x
**License**: BSD 3-Clause
**Downloads**: ~5M

| Criterion | Assessment |
|---|---|
| JavaScript support | ES2019+ |
| TypeScript support | No |
| Vue SFC support | No |
| AST quality | Good |
| Maintenance | Active (Sébastien Ros) |

**Verdict: Rejected.** No TypeScript support is the dealbreaker. All modern projects in this
category mix `.ts`, `.tsx`, and `.vue` files. A JS-only parser covers less than half the codebase.

---

### Option 2: Acornima

**NuGet**: `Acornima`
**Version**: 0.6.x
**License**: MIT

| Criterion | Assessment |
|---|---|
| JavaScript support | ES2023+ |
| TypeScript support | No |
| Vue SFC support | No |
| AST quality | Good — actively maintained fork of Esprima |
| Maintenance | Active |

**Verdict: Rejected.** Same reason as Esprima: no TypeScript support. Acornima is the better
JS parser between the two, but TypeScript is the gap.

---

### Option 3: Jint

**NuGet**: `Jint`
**Version**: 3.x
**License**: BSD 2-Clause

| Criterion | Assessment |
|---|---|
| Purpose | JavaScript interpreter |
| AST access | Indirectly via underlying Esprima |
| TypeScript support | No |
| Suitable for static analysis | No — designed for execution, not analysis |

**Verdict: Rejected.** Wrong tool for the job. Jint is an interpreter; we need a parser.

---

### Option 4: NiL.JS

**NuGet**: `NiL.JS`
**Version**: 2.5.x
**License**: BSD 3-Clause

| Criterion | Assessment |
|---|---|
| Maturity | Low — limited adoption |
| TypeScript support | No |
| Maintenance | Uncertain (infrequent releases) |

**Verdict: Rejected.** Maturity and maintenance concerns. Same TypeScript gap as others.

---

### Option 5: Custom Regex Parser (Recommended)

**NuGet**: None

| Criterion | Assessment |
|---|---|
| JavaScript support | Yes |
| TypeScript support | Yes (regex targets name extraction, not type resolution) |
| Vue SFC support | Yes (via VueSfcExtractor) |
| Accuracy | 85-90% for structural extraction |
| Startup cost | <10ms |
| Maintenance | In-repo |

**Verdict: Recommended.** Consistent with Python approach. Handles all required file types.
See `PLAN-JAVASCRIPT.md` for full rationale and key regex patterns.

---

### System.Text.Json (for package.json)

Already part of the .NET framework — no additional NuGet reference needed. Used to deserialize
`package.json` and `tsconfig.json`.

---

## Cross-Platform Verification

| Component | Windows | Linux | macOS | Docker |
|---|---|---|---|---|
| `CodeIntelligenceMcp.Python` | Yes | Yes | Yes | Yes |
| `CodeIntelligenceMcp.JavaScript` | Yes | Yes | Yes | Yes |
| `Tomlyn` | Yes | Yes | Yes | Yes |
| Custom regex parsers | Yes | Yes | Yes | Yes |
| `VueSfcExtractor` | Yes | Yes | Yes | Yes |

All new components are pure .NET with no native code. Docker images require only the .NET 10
runtime — no additional system packages.

---

## Full NuGet Package List

### CodeIntelligenceMcp.Python.csproj

```xml
<PackageReference Include="Tomlyn" Version="0.17.*" />
```

### CodeIntelligenceMcp.JavaScript.csproj

No new packages. `System.Text.Json` is part of `Microsoft.NETCore.App`.

### Summary of New Packages

| Package | Version | License | Size | Used For |
|---|---|---|---|---|
| `Tomlyn` | 0.17.x | BSD 2-Clause | ~80KB | `pyproject.toml` TOML parsing |
