# PLAN-PYTHON.md — Python Analyzer Implementation Plan

## Overview

Add Python codebase analysis to CodeIntelligenceMcp following the exact pattern established by
`CodeIntelligenceMcp.PowerShell`. No external runtime dependencies.

---

## Parser Decision: Custom Regex/String Parser

### Comparison

| Criterion | Custom Regex | IronPython | Python.NET |
|---|---|---|---|
| External deps | None | IronPython NuGet | Python runtime installed |
| Cross-platform | Yes | Yes | Fragile (path setup) |
| Python 3 support | Full | Limited subset | Full |
| Startup cost | <10ms | ~300ms | ~500ms+ |
| Maintenance | In-repo | Third-party | Third-party |
| Accuracy for wiki | 85-90% | 95%+ | 95%+ |

**Recommendation: Custom regex parser.**

85-90% accuracy is acceptable because the goal is a compact structural wiki, not semantic analysis.
Python's `def`/`class` syntax is highly regular. The cases that fail (dynamic `setattr`,
metaclass-generated methods) are not meaningful to surface in a wiki. This mirrors how
`VbscriptParserAdapter` works: regex is the right tool when there is no .NET-native AST parser
and the accuracy bar is structural overview rather than full program understanding.

IronPython requires running Python code inside the interpreter to get AST — it is primarily a
runtime, not a parsing library. Python.NET requires a Python installation on the host machine, which
is an unacceptable external dependency for a tool that must "just work."

**`pyproject.toml` exception**: Use `Tomlyn` NuGet for TOML parsing. Nested TOML tables
(`[project.optional-dependencies]`, `[tool.poetry.dependencies]`) require correct TOML
interpretation that regex cannot reliably provide. `Tomlyn` is pure .NET, ~50KB, MIT license.

---

## What to Parse

### `.py` files
- Module-level docstring (first string literal at file top)
- Imports: `import x`, `from x import y, z`, `from . import z` (relative)
- Top-level functions: name, parameters (with type hints), return type, decorators, async flag, line range
- Classes: name, base classes, methods (name + parameter list), decorators, line range
- `__all__` list (exported names)
- Framework pattern markers (see below)

### `requirements.txt`
- Package name and version constraint per line
- Skip comments (`#`), blank lines, `-r` includes, URL-based installs

### `pyproject.toml` (via Tomlyn)
- `[project].name`, `[project].version`
- `[project].dependencies` (list of package strings)
- `[project.optional-dependencies]` (dev, test, etc.)
- `[tool.poetry.dependencies]` and `[tool.poetry.dev-dependencies]` (Poetry style)

### `setup.cfg` (fallback)
- `[options] install_requires` section (INI-style line scanning)

### Framework detection (in `.py` files)

| Import/Pattern | Detected As |
|---|---|
| `from fastapi import` | FastAPI |
| `@app.get`, `@router.post`, etc. | FastAPI routes |
| `from flask import Flask` | Flask |
| `from django` | Django |
| `models.Model` (with django import) | Django models |
| `BaseModel` (with pydantic import) | Pydantic models |
| `from sqlalchemy` | SQLAlchemy |
| `import pytest` or `from pytest` | Pytest |
| `async def` (count) | Async usage |

---

## File Structure

```
src/CodeIntelligenceMcp.Python/
    CodeIntelligenceMcp.Python.csproj       # references Tomlyn
    Models/
        PythonFileInfo.cs                   # all model records in one file (small enough)
    PythonFileParser.cs                     # parses .py files → PythonFileInfo
    PythonPackageParser.cs                  # requirements.txt, pyproject.toml, setup.cfg
    PythonIndex.cs                          # Build(rootPath, log), query methods
    PythonWikiGenerator.cs                  # Generate(focusArea, includePatterns, includeMetrics)

src/CodeIntelligenceMcp/
    Tools/PythonTools.cs                    # [McpServerToolType] MCP tool handlers
```

---

## Model Records

```csharp
// Models/PythonFileInfo.cs
namespace CodeIntelligenceMcp.Python.Models;

public sealed record PythonParameterInfo(
    string Name,
    string? TypeHint,
    string? DefaultValue);

public sealed record PythonFunctionInfo(
    string Name,
    int LineStart,
    int LineEnd,
    IReadOnlyList<PythonParameterInfo> Parameters,
    string? ReturnTypeHint,
    IReadOnlyList<string> Decorators,
    bool IsAsync,
    bool IsMethod);   // true when defined inside a class

public sealed record PythonClassInfo(
    string Name,
    int LineStart,
    int LineEnd,
    IReadOnlyList<string> BaseClasses,
    IReadOnlyList<PythonFunctionInfo> Methods,
    IReadOnlyList<string> Decorators);

public sealed record PythonImportInfo(
    string Module,
    IReadOnlyList<string> Names,   // empty for "import X", populated for "from X import a, b"
    bool IsRelative,               // starts with "."
    int Line);

public sealed record PythonFileInfo(
    string FilePath,
    IReadOnlyList<PythonFunctionInfo> Functions,       // top-level only
    IReadOnlyList<PythonClassInfo> Classes,
    IReadOnlyList<PythonImportInfo> Imports,
    IReadOnlyList<string> ExportedNames,               // from __all__
    IReadOnlyList<string> DetectedFrameworks);

public sealed record PythonPackageInfo(
    string Name,
    string? VersionConstraint,
    string Source);   // "requirements.txt", "pyproject.toml", "setup.cfg"

public sealed record PythonProjectInfo(
    string? ProjectName,
    string? PythonVersion,
    IReadOnlyList<PythonPackageInfo> Dependencies,
    IReadOnlyList<PythonPackageInfo> DevDependencies);
```

---

## PythonFileParser — Key Parsing Logic

Multi-line function signatures (parameters spanning lines) are handled by scanning forward
from the opening `(` character-by-character until the balancing `)` is found, tracking depth.
This is the same technique used in `AspBlockExtractor` for `<% %>` block extraction.

Indentation tracking is used to distinguish class methods from top-level functions:
record the indent level of each `class` definition; a `def` that starts at a greater indent
level immediately following a class definition is treated as a method.

### Core Regex Patterns

```csharp
// Function definition start
private static readonly Regex FunctionDef =
    new(@"^(\s*)(async\s+)?def\s+(\w+)\s*\(", RegexOptions.Compiled | RegexOptions.Multiline);

// Class definition
private static readonly Regex ClassDef =
    new(@"^(\s*)class\s+(\w+)\s*(?:\(([^)]*)\))?:", RegexOptions.Compiled | RegexOptions.Multiline);

// Decorator line
private static readonly Regex Decorator =
    new(@"^(\s*)@(\S+)", RegexOptions.Compiled | RegexOptions.Multiline);

// Import: "import x" or "import x, y"
private static readonly Regex ImportSimple =
    new(@"^import\s+(.+)$", RegexOptions.Compiled | RegexOptions.Multiline);

// Import: "from x import y, z"
private static readonly Regex ImportFrom =
    new(@"^from\s+([.\w]+)\s+import\s+(.+)$", RegexOptions.Compiled | RegexOptions.Multiline);

// __all__ export list (single-line)
private static readonly Regex AllExports =
    new(@"^__all__\s*=\s*\[([^\]]+)\]", RegexOptions.Compiled | RegexOptions.Multiline);

// Return type hint "-> SomeType:"
private static readonly Regex ReturnHint =
    new(@"->\s*([\w\[\],\s|.]+)\s*:", RegexOptions.Compiled);
```

---

## PythonIndex — Public API

```csharp
public sealed class PythonIndex
{
    public int FileCount { get; }
    public string RootPath { get; }

    // Follows the exact same pattern as PowerShellIndex.Build
    public static PythonIndex Build(string rootPath, Action<string>? log = null);

    public PythonFileInfo? GetFile(string filePath);

    public IReadOnlyList<(string FilePath, PythonFunctionInfo Function)> FindFunction(
        string functionName);   // case-insensitive substring match

    public IReadOnlyList<(string FilePath, PythonClassInfo Class)> FindClass(
        string className);      // case-insensitive substring match

    public IReadOnlyList<(string FilePath, int Line, string Context)> Search(string query);

    public PythonProjectInfo GetProjectInfo();

    public IReadOnlyDictionary<string, PythonFileInfo> GetAllFiles();
}
```

### Build Logic (skip directories)

```csharp
private static readonly string[] SkipDirs =
    ["__pycache__", ".venv", "venv", "env", ".git", ".tox", ".eggs",
     "node_modules", ".pytest_cache", "dist", "build", "*.egg-info"];
```

---

## MCP Tools (PythonTools.cs)

Follows `PowerShellTools.cs` pattern exactly: primary constructor takes `PythonIndexRegistry`,
has `Ok()`/`Err()` helpers returning JSON.

```csharp
[McpServerTool(Name = "get_python_wiki")]
[Description("Generate a compact overview of a Python project: modules, classes, functions, imports, dependencies, and framework patterns.")]
public string GetPythonWiki(
    [Description("Workspace name")] string workspace,
    [Description("Subdirectory or module prefix to focus on (e.g. 'src/api' or 'myapp')")] string? focusArea = null,
    [Description("Include pattern analysis (async usage, Pydantic models, FastAPI routes, etc.)")] bool includePatterns = true,
    [Description("Include metrics (file counts, class/function counts)")] bool includeMetrics = false)

[McpServerTool(Name = "py_get_file")]
[Description("Get the full analysis of a single Python file: functions, classes, imports, exports.")]
public string PyGetFile(
    [Description("Workspace name")] string workspace,
    [Description("File path (absolute or relative to workspace root)")] string filePath)

[McpServerTool(Name = "py_find_function")]
[Description("Find Python functions and methods by name across all files in the workspace.")]
public string PyFindFunction(
    [Description("Workspace name")] string workspace,
    [Description("Function name or partial name (case-insensitive)")] string functionName)

[McpServerTool(Name = "py_find_class")]
[Description("Find Python classes by name across all files in the workspace.")]
public string PyFindClass(
    [Description("Workspace name")] string workspace,
    [Description("Class name or partial name (case-insensitive)")] string className)

[McpServerTool(Name = "py_search")]
[Description("Search for a term across function names, class names, and import paths in a Python workspace.")]
public string PySearch(
    [Description("Workspace name")] string workspace,
    [Description("Search term")] string query)
```

---

## Config

```json
{
  "name": "my-fastapi-project",
  "type": "python",
  "rootPath": "C:/Projects/MyFastApiApp"
}
```

Program.cs addition (new branch in workspace loading loop, same pattern as "powershell"):

```csharp
else if (ws.Type == "python" && ws.RootPath is not null)
{
    PythonIndex index = PythonIndex.Build(ws.RootPath, msg => Console.Error.WriteLine(msg));
    pyIndexes[ws.Name] = index;
    Console.Error.WriteLine($"[info] Workspace '{ws.Name}' loaded — {index.FileCount} files");
}
```

---

## Implementation Effort

| Task | Hours |
|---|---|
| Project scaffold + model records | 1 |
| `PythonFileParser` (regex + indent tracking + multi-line sig scan) | 3 |
| `PythonPackageParser` (requirements.txt + pyproject.toml via Tomlyn + setup.cfg) | 2 |
| Framework pattern detection | 1 |
| `PythonIndex` (build + query methods) | 1.5 |
| `PythonWikiGenerator` | 2 |
| `PythonTools` + registry + Program.cs wiring | 1 |
| Tests (xUnit + FluentAssertions) | 3 |
| **Total** | **~14.5h** |

**Complexity**: Medium — regex multi-line scanning has edge cases.
**Risk**: Low — zero external runtime dependencies, pure .NET.

---

## Risk Factors & Mitigations

| Risk | Likelihood | Mitigation |
|---|---|---|
| Multi-line function signatures with complex type hints (e.g. `dict[str, list[int]]`) | Medium | Extract signature as raw string; do not try to structurally parse nested generics |
| Indentation-based method detection mis-classifies nested functions | Low | Only track one level deep; nested functions within methods are not indexed |
| `pyproject.toml` format varies (PEP 621 vs Poetry vs Flit) | Medium | Handle all three sections; Tomlyn reads them uniformly regardless of section name |
| Large repos (Django monorepo with 2000+ .py files) | Low | File read is fast; no compilation step needed — should be <2s for most projects |
| `__all__` multi-line format (`__all__ = [\n    "foo",\n    "bar"\n]`) | Medium | After failing single-line regex, scan forward until matching `]` bracket |

---

## Go/No-Go Recommendation

**Go.** Zero external runtime dependencies, fits the existing pattern exactly, and delivers
meaningful value for FastAPI/Django/script-heavy repos. Parser accuracy is sufficient for
the structural wiki use case.
