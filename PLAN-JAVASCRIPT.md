# PLAN-JAVASCRIPT.md — JavaScript/TypeScript Analyzer Implementation Plan

## Overview

Add JavaScript/TypeScript (including Vue SFC and Nuxt) codebase analysis to CodeIntelligenceMcp.
Follows the existing per-language pattern. No external runtime dependencies.

---

## Parser Decision: Custom Regex/String Parser

### Comparison

| Criterion | Custom Regex | Esprima.NET / Acornima | Jint | NiL.JS |
|---|---|---|---|---|
| External deps | None | NuGet package | NuGet package | NuGet package |
| TypeScript support | Yes (regex) | No — JS only | No | No |
| Vue SFC support | Yes (custom) | No | No | No |
| Cross-platform | Yes | Yes | Yes | Yes |
| Accuracy for wiki | 85-90% | 95% (JS only) | N/A | Low maturity |
| Maintenance | In-repo | Active | Active | Uncertain |

**Recommendation: Custom regex parser.**

Esprima.NET and Acornima are well-maintained and accurate for JavaScript — but neither handles
TypeScript natively. Adding TypeScript support would require a separate preprocessing step to
strip type annotations, which reintroduces the regex complexity we were trying to avoid, and
still wouldn't handle `.vue` files.

Jint is a JavaScript interpreter, not a static analysis tool. It is the wrong tool for this job.

A custom regex parser delivers consistent accuracy across `.js`, `.ts`, `.jsx`, `.tsx`, and the
script blocks of `.vue` files. For the structural wiki use case (names, imports, exports) this
is sufficient — we are not resolving types or tracking data flow.

**Vue SFC extraction**: Separate `VueSfcExtractor` class modeled on `AspBlockExtractor`.
Character-by-character scanning for `<template>`, `<script>`, and `<style>` tags with
`lang` and `setup` attribute extraction. The extracted `<script>` content feeds into `JsFileParser`.

**package.json parsing**: Use `System.Text.Json` (already available in the framework). No
additional NuGet packages required.

---

## What to Parse

### `.js` / `.ts` / `.jsx` / `.tsx` files
- ESM imports: `import { x } from 'y'`, `import * as x from 'y'`, `import x from 'y'`
- CommonJS: `const x = require('y')`, `module.exports = ...`
- Named function declarations: `function foo(...)`, `async function foo(...)`
- Arrow functions assigned to `const`/`let`: `const foo = (x) => ...`
- Class declarations: `class Foo extends Bar`
- Exports: `export function`, `export class`, `export const`, `export default`, `export { x, y }`
- TypeScript interfaces: `interface Foo extends Bar`
- TypeScript type aliases: `type Foo = ...`
- TypeScript enums: `enum Direction`

### `.vue` files (Vue SFC)
- Extract `<template>`, `<script>`, `<style>` blocks with their `lang` and `setup` attributes
- Parse extracted `<script>` block with `JsFileParser`
- Detect Composition API patterns: `defineProps`, `defineEmits`, `defineExpose`
- Detect composables used: `use*()` function calls

### `package.json`
- `name`, `version`, `description`
- `dependencies` (production packages with versions)
- `devDependencies`
- `scripts` keys (build, dev, test, etc.)

### `tsconfig.json`
- `compilerOptions.target`, `compilerOptions.strict`
- `compilerOptions.paths` (path aliases)

### Framework detection

| Pattern | Detected As |
|---|---|
| `"vue"` in dependencies or `import { ... } from 'vue'` | Vue 3 |
| `nuxt.config.ts` present or `"nuxt"` in deps | Nuxt |
| `"react"` in dependencies | React |
| `"next"` in dependencies | Next.js |
| `"express"` in dependencies | Express |
| `"@nestjs/core"` in dependencies | NestJS |
| `"@tanstack/query"` or `"@tanstack/vue-query"` | TanStack Query |
| `"pinia"` in dependencies | Pinia (state management) |

### Nuxt conventions (directory-based)
- `pages/` — file-based routing
- `composables/` — auto-imported composables
- `stores/` — Pinia stores
- `layouts/` — layout components
- `middleware/` — route middleware
- `server/api/` — server API routes
- `server/routes/` — server routes
- `plugins/` — Nuxt plugins

---

## File Structure

```
src/CodeIntelligenceMcp.JavaScript/
    CodeIntelligenceMcp.JavaScript.csproj   # no external NuGet deps
    Models/
        JsFileInfo.cs                       # all JS/TS model records
        VueSfcInfo.cs                       # Vue SFC model records
    JsFileParser.cs                         # parses .js/.ts/.jsx/.tsx → JsFileInfo
    VueSfcExtractor.cs                      # extracts blocks from .vue files
    JsPackageParser.cs                      # package.json, tsconfig.json
    JsIndex.cs                              # Build(rootPath, log), query methods
    JsWikiGenerator.cs                      # Generate(focusArea, includePatterns, includeMetrics)

src/CodeIntelligenceMcp/
    Tools/JsTools.cs                        # [McpServerToolType] MCP tool handlers
```

---

## Model Records

### JsFileInfo.cs

```csharp
namespace CodeIntelligenceMcp.JavaScript.Models;

public sealed record JsParameterInfo(
    string Name,
    string? TypeAnnotation,
    string? DefaultValue);

public sealed record JsFunctionInfo(
    string Name,
    int LineStart,
    int LineEnd,
    IReadOnlyList<JsParameterInfo> Parameters,
    string? ReturnType,
    bool IsExported,
    bool IsAsync,
    bool IsGenerator,
    string Kind);         // "declaration", "arrow", "method"

public sealed record JsClassInfo(
    string Name,
    int LineStart,
    int LineEnd,
    string? Extends,
    IReadOnlyList<string> Implements,           // TypeScript only
    IReadOnlyList<JsFunctionInfo> Methods,
    bool IsExported,
    bool IsAbstract);                           // TypeScript only

public sealed record JsImportInfo(
    string Source,                              // the module path/package
    IReadOnlyList<string> NamedImports,
    string? DefaultImport,
    string? NamespaceImport,                    // import * as X
    bool IsTypeOnly,                            // TypeScript "import type"
    int Line);

public sealed record JsExportInfo(
    string Name,
    string Kind,                                // "function", "class", "const", "default", "re-export"
    bool IsTypeOnly,
    int Line);

public sealed record JsInterfaceInfo(           // TypeScript only
    string Name,
    int LineStart,
    int LineEnd,
    IReadOnlyList<string> Extends,
    bool IsExported);

public sealed record JsTypeAliasInfo(           // TypeScript only
    string Name,
    int Line,
    bool IsExported);

public sealed record JsEnumInfo(                // TypeScript only
    string Name,
    int LineStart,
    int LineEnd,
    bool IsExported,
    bool IsConst);

public sealed record JsFileInfo(
    string FilePath,
    IReadOnlyList<JsFunctionInfo> Functions,
    IReadOnlyList<JsClassInfo> Classes,
    IReadOnlyList<JsImportInfo> Imports,
    IReadOnlyList<JsExportInfo> Exports,
    IReadOnlyList<JsInterfaceInfo> Interfaces,
    IReadOnlyList<JsTypeAliasInfo> TypeAliases,
    IReadOnlyList<JsEnumInfo> Enums,
    string ModuleType);                         // "esm", "commonjs", "mixed", "none"

public sealed record JsPackageInfo(
    string Name,
    string? Version,
    bool IsDev);

public sealed record JsProjectInfo(
    string? ProjectName,
    string? Version,
    IReadOnlyList<JsPackageInfo> Dependencies,
    IReadOnlyList<string> Scripts,
    IReadOnlyList<string> DetectedFrameworks);
```

### VueSfcInfo.cs

```csharp
namespace CodeIntelligenceMcp.JavaScript.Models;

public sealed record VueSfcBlock(
    string Tag,           // "template", "script", "style"
    string? Lang,         // "ts", "scss", "pug", null for default
    bool IsSetup,         // <script setup>
    int LineStart,
    int LineEnd,
    string Content);

public sealed record VueSfcInfo(
    string FilePath,
    IReadOnlyList<VueSfcBlock> Blocks,
    JsFileInfo? ScriptAnalysis,
    IReadOnlyList<string> Props,       // from defineProps
    IReadOnlyList<string> Emits,       // from defineEmits
    IReadOnlyList<string> Composables); // use*() calls detected
```

---

## VueSfcExtractor Design

Modeled directly on `AspBlockExtractor`. Character-by-character scan of the file content.
Tracks line numbers throughout.

```
State machine:
  - Scanning outside a block: look for "<" followed by "template", "script", or "style"
  - Read the opening tag attributes: extract lang="..." and presence of "setup"
  - Scan until the matching closing tag </template>, </script>, </style>
  - Emit a VueSfcBlock with lineStart, lineEnd, content, lang, isSetup
```

Edge cases to handle:
- `<script setup lang="ts">` — both `setup` and `lang` attributes present
- `<script>` and `<script setup>` in the same file (Vue 3 allows this for options + setup)
- Self-closing tags (not valid in SFC but handle gracefully)
- Content inside `<template>` may contain `</div>` — only stop on `</template>` at the root level

---

## JsIndex — Public API

```csharp
public sealed class JsIndex
{
    public int FileCount { get; }
    public string RootPath { get; }

    public static JsIndex Build(string rootPath, Action<string>? log = null);

    public JsFileInfo? GetFile(string filePath);

    public IReadOnlyList<(string FilePath, JsFunctionInfo Function)> FindFunction(
        string functionName);

    public IReadOnlyList<(string FilePath, JsClassInfo Class)> FindClass(
        string className);

    public IReadOnlyList<(string FilePath, int Line, string Context)> Search(string query);

    public JsProjectInfo GetProjectInfo();

    public IReadOnlyList<VueSfcInfo> GetVueComponents();

    public IReadOnlyDictionary<string, JsFileInfo> GetAllFiles();
}
```

### Build Logic

File extensions to include: `.js`, `.ts`, `.jsx`, `.tsx`, `.mjs`, `.cjs`
Vue files: `.vue` (processed separately via `VueSfcExtractor`)

Skip directories:
```csharp
private static readonly string[] SkipDirs =
    ["node_modules", ".git", "dist", "build", ".next", ".nuxt", ".output",
     "coverage", ".nyc_output", "vendor", ".cache", "tmp", "__snapshots__"];
```

---

## MCP Tools (JsTools.cs)

```csharp
[McpServerTool(Name = "get_js_wiki")]
[Description("Generate a compact overview of a JavaScript/TypeScript project: modules, components, exports, imports, dependencies, and framework patterns.")]
public string GetJsWiki(
    [Description("Workspace name")] string workspace,
    [Description("Subdirectory to focus on (e.g. 'src/components' or 'server/api')")] string? focusArea = null,
    [Description("Include pattern analysis (Vue SFC, Nuxt conventions, React components, etc.)")] bool includePatterns = true,
    [Description("Include metrics (file counts, function/class/interface counts)")] bool includeMetrics = false)

[McpServerTool(Name = "js_get_file")]
[Description("Get the full analysis of a single JS/TS/Vue file: functions, classes, imports, exports, interfaces.")]
public string JsGetFile(
    [Description("Workspace name")] string workspace,
    [Description("File path (absolute or relative to workspace root)")] string filePath)

[McpServerTool(Name = "js_find_function")]
[Description("Find JavaScript/TypeScript functions by name across all files in the workspace.")]
public string JsFindFunction(
    [Description("Workspace name")] string workspace,
    [Description("Function name or partial name (case-insensitive)")] string functionName)

[McpServerTool(Name = "js_find_class")]
[Description("Find JavaScript/TypeScript classes and Vue components by name across all files in the workspace.")]
public string JsFindClass(
    [Description("Workspace name")] string workspace,
    [Description("Class or component name or partial name (case-insensitive)")] string className)

[McpServerTool(Name = "js_search")]
[Description("Search for a term across function names, class names, exports, and import paths in a JavaScript/TypeScript workspace.")]
public string JsSearch(
    [Description("Workspace name")] string workspace,
    [Description("Search term")] string query)
```

---

## Config

```json
{
  "name": "my-nuxt-app",
  "type": "javascript",
  "rootPath": "C:/Projects/MyNuxtApp"
}
```

Program.cs addition:
```csharp
else if (ws.Type == "javascript" && ws.RootPath is not null)
{
    JsIndex index = JsIndex.Build(ws.RootPath, msg => Console.Error.WriteLine(msg));
    jsIndexes[ws.Name] = index;
    Console.Error.WriteLine($"[info] Workspace '{ws.Name}' loaded — {index.FileCount} files");
}
```

---

## Key Regex Patterns (JsFileParser)

```csharp
// Named function declaration
private static readonly Regex FunctionDecl =
    new(@"^(?:export\s+)?(?:default\s+)?(?:async\s+)?function\s*\*?\s*(\w+)\s*\(",
        RegexOptions.Compiled | RegexOptions.Multiline);

// Arrow function assigned to const/let
private static readonly Regex ArrowFunction =
    new(@"^(?:export\s+)?(?:const|let)\s+(\w+)\s*=\s*(?:async\s+)?\(",
        RegexOptions.Compiled | RegexOptions.Multiline);

// Class declaration
private static readonly Regex ClassDecl =
    new(@"^(?:export\s+)?(?:default\s+)?(?:abstract\s+)?class\s+(\w+)(?:\s+extends\s+(\w+))?",
        RegexOptions.Compiled | RegexOptions.Multiline);

// TypeScript interface
private static readonly Regex InterfaceDecl =
    new(@"^(?:export\s+)?interface\s+(\w+)(?:\s+extends\s+([\w,\s<>]+))?",
        RegexOptions.Compiled | RegexOptions.Multiline);

// TypeScript type alias
private static readonly Regex TypeAlias =
    new(@"^(?:export\s+)?type\s+(\w+)\s*(?:<[^>]*>)?\s*=",
        RegexOptions.Compiled | RegexOptions.Multiline);

// TypeScript enum
private static readonly Regex EnumDecl =
    new(@"^(?:export\s+)?(?:const\s+)?enum\s+(\w+)",
        RegexOptions.Compiled | RegexOptions.Multiline);

// ESM import
private static readonly Regex EsmImport =
    new(@"^import\s+(?:type\s+)?(?:{([^}]+)}|(\w+)|\*\s+as\s+(\w+)).*from\s+['""]([^'""]+)['""]",
        RegexOptions.Compiled | RegexOptions.Multiline);

// CommonJS require
private static readonly Regex CjsRequire =
    new(@"(?:const|let|var)\s+(?:{([^}]+)}|(\w+))\s*=\s*require\(['""]([^'""]+)['""]\)",
        RegexOptions.Compiled);
```

---

## Implementation Effort

| Task | Hours |
|---|---|
| Project scaffold + model records | 1 |
| `JsFileParser` (regex for JS, TS, JSX, TSX) | 4 |
| `VueSfcExtractor` (tag scanning + attribute extraction) | 2 |
| Vue Composition API pattern detection (defineProps, composables) | 1.5 |
| `JsPackageParser` (package.json + tsconfig.json) | 1 |
| Nuxt directory convention detection | 1 |
| `JsIndex` (build + query methods) | 1.5 |
| `JsWikiGenerator` | 2 |
| `JsTools` + registry + Program.cs wiring | 1 |
| Tests (xUnit + FluentAssertions) | 4 |
| **Total** | **~19h** |

**Complexity**: Medium-High — Vue SFC edge cases, JS/TS dual-mode parsing, ESM/CJS detection.
**Risk**: Medium — Vue SFC has real edge cases; `<script setup>` with generics can trip regex.

---

## Risk Factors & Mitigations

| Risk | Likelihood | Mitigation |
|---|---|---|
| Arrow functions with TypeScript generic `<T>` parameters trip regex | Medium | Strip `<...>` generics from name-extraction lines before applying arrow function regex |
| Vue SFC with `<script>` and `<script setup>` (dual script blocks) | Low | Handle by collecting all `<script>` blocks; mark each with `IsSetup` flag |
| `<template>` containing `</script>` in an attribute value | Very Low | Only detect `</script>` at start of a line (after stripping leading whitespace) |
| `node_modules` accidentally scanned if symlinked | Low | Check `Directory.GetAttributes` for junction/symlink before recursing |
| Large Nuxt app with 500+ Vue components | Low | File reads are fast; no compilation step — <2s expected |
| Dynamic `require()` calls in variables | Low | Only extract literal string requires — acceptable for wiki |

---

## Go/No-Go Recommendation

**Go.** No external runtime dependencies, consistent with the existing pattern, and delivers
high value for Vue/Nuxt and React/Next.js codebases. Vue SFC edge cases are manageable with
careful `VueSfcExtractor` implementation modeled on `AspBlockExtractor`.
