# Question: Add PowerShell Script Analysis to CodeIntelligenceMcp

## Context

I want to extend CodeIntelligenceMcp to analyze PowerShell script projects (`.ps1`, `.psm1`, `.psd1` files) similar to how we currently analyze C#/.NET codebases with Roslyn.

**Goal**: Generate a compact wiki overview of PowerShell projects showing:
- Functions and their parameters
- Module structure
- Cmdlet usage patterns
- Dependencies (imported modules)
- Variables and configuration

**Why**: Same token-saving benefits as `get_codebase_wiki` but for PowerShell deployment scripts, automation projects, etc.

## Current Implementation to Reference

**Please examine the existing code:**

1. **Roslyn Analysis Implementation**
   - `Services/RoslynAnalyzer.cs` - How do we currently load and analyze C# solutions?
   - What patterns/structure can we reuse for PowerShell?

2. **Wiki Generation Pattern**
   - `Services/WikiGenerator.cs` - How do we build compact overviews?
   - `Tools/CodebaseWikiTool.cs` - Tool signature and parameter handling
   - What abstraction can we create for language-agnostic wiki generation?

3. **DI Registration**
   - `Program.cs` or startup - How are services registered?
   - Where would PowerShell analyzer fit in?

## Technical Approach Options

**Option A: PowerShell AST Parser**
Use `System.Management.Automation.Language` namespace:
```csharp
using System.Management.Automation.Language;

var ast = Parser.ParseInput(scriptContent, out tokens, out errors);
var functions = ast.FindAll(a => a is FunctionDefinitionAst, true);
```

**Option B: Invoke PowerShell for Analysis**
Run `Get-Command`, `Get-Module`, etc. via `PowerShell.Create()`:
```csharp
using var ps = PowerShell.Create();
ps.AddScript("Get-Command -Module MyModule");
```

**Option C: Regex-based (simpler but less accurate)**
Pattern matching for functions, params, etc.

## Questions for You

1. **Architecture**: Looking at the current Roslyn implementation, should PowerShell analysis:
   - Be a separate `PowerShellAnalyzer` service parallel to `RoslynAnalyzer`?
   - Share infrastructure with `WikiGenerator`?
   - Have its own `PowerShellWikiTool` or extend `CodebaseWikiTool` with language detection?

2. **Parser Choice**: Which approach fits best with the existing codebase?
   - Option A (AST) seems most aligned with Roslyn approach
   - What are the tradeoffs you see?

3. **Tool Design**: Should it be:
   - Separate tool: `get_powershell_wiki`
   - Unified tool: `get_codebase_wiki` with auto-detection (`.sln` → Roslyn, `.ps1` → PowerShell)
   - Both?

4. **Scope**: What PowerShell artifacts should we analyze?
   - `.ps1` scripts (functions, workflow)
   - `.psm1` modules (exported functions)
   - `.psd1` manifests (module metadata, dependencies)
   - `pester` tests?

5. **Dependencies**: What NuGet packages are needed?
   - `Microsoft.PowerShell.SDK`?
   - `System.Management.Automation`?
   - Check compatibility with existing packages

## Desired Output Format

Similar to C# wiki but PowerShell-specific:

```
# PowerShell Project Wiki

## Scripts/
  └─ Deploy-Application.ps1
     Functions: Initialize-Deployment, Deploy-Services, Rollback-Deployment
     Parameters: $Environment, $Version, $ServiceList
     External Modules: Az.Accounts, Az.Resources
  
  └─ Helpers/Logger.psm1
     Exported Functions: Write-Log, Set-LogLevel
     
## Modules Imported
- Az.Accounts (v2.12.0)
- Az.Resources (v6.7.0)

## Patterns Detected
- Advanced Functions: 8 with [CmdletBinding()]
- Pipeline Support: 5 functions
- Error Handling: Try/Catch in 12 functions
```

## Action Items

Please:
1. Review existing Roslyn/Wiki implementation
2. Recommend architecture approach that fits current patterns
3. Identify code that can be shared/abstracted
4. Suggest concrete file structure and class names
5. Note any potential issues or incompatibilities
6. Provide a TASK.md outline for implementation

## Success Criteria

- Minimal code duplication with existing Roslyn analysis
- Consistent tool interface (parameters, output format)
- Fast analysis (<2s for typical PowerShell project)
- Accurate function/parameter extraction
- Easy to extend later (e.g., add pester test analysis)
