# TASK: Add Codebase Wiki Tool to CodeIntelligenceMcp

## Context
Add a new MCP tool that generates a compact, hierarchical overview of the codebase inspired by CodeSight's approach. This gives Claude Code CLI instant context about project structure, patterns, and architecture in ~200 tokens instead of 5000+.

## Objective
Implement `get_codebase_wiki` tool that leverages existing Roslyn analysis to generate an in-memory wiki overview.

## Implementation Steps

### 1. Create WikiGenerator Service
**File**: `Services/WikiGenerator.cs`

**Responsibilities**:
- Analyze architectural patterns (IUseCase, Result<T>, repositories)
- Detect vertical slices from namespaces
- Calculate metrics (type counts, LOC, test coverage)
- Extract domain names from namespace structure

**Key Methods**:
- `Task<string> AnalyzePatternsAsync(Solution, string? focusArea)`
- `Task<string> CalculateMetricsAsync(Solution, string? focusArea)`
- `Task<List<INamedTypeSymbol>> GetAllTypesAsync(Solution, string? focusArea)`
- `string GetDomainFromNamespace(string namespaceString)`

### 2. Create CodebaseWikiTool
**File**: `Tools/CodebaseWikiTool.cs`

**Tool Signature**:
```csharp
[Tool("get_codebase_wiki")]
public async Task<string> GetCodebaseWiki(
    string? focusArea = null,
    bool includePatterns = true,
    bool includeMetrics = false)
```

**Functionality**:
- Generate hierarchical project structure
- Group types by namespace with breakdown (classes, interfaces, records, enums)
- Optional pattern analysis section
- Optional metrics section
- Filter by focusArea (namespace or project name)

### 3. Add Roslyn Extension Methods
**In**: `Tools/CodebaseWikiTool.cs` (or separate extensions file)

**Methods**:
- `IEnumerable<INamedTypeSymbol> GetAllTypes(INamespaceSymbol)`
- `IEnumerable<INamedTypeSymbol> GetNestedTypes(INamedTypeSymbol)`

### 4. Register Services
**File**: `Program.cs` or `Startup.cs`

Add to DI container:
```csharp
services.AddSingleton<WikiGenerator>();
services.AddSingleton<CodebaseWikiTool>();
```

Register tool with MCP server.

## Expected Output Format

```
# Codebase Wiki

Generated: 2026-04-08 14:30:00

## Project Structure

### Datalake2.Core
  └─ Domain.Entities/ [12 classes, 0 interfaces]
  └─ Domain.ValueObjects/ [8 classes, 0 interfaces]
  └─ Application.Orders/ [8 classes, 8 interfaces, 1 record]

### Datalake2.API
  └─ Controllers/ [13 classes]

## Architectural Patterns

**Use Cases**: 45 implementations
  - Orders: 8 use cases
  - Invoices: 6 use cases

**Result<T> Pattern**: 156 method usages

**Repositories**: 8 implementations
  - Dapper-based: ~5
  - EF Core-based: ~3

**Vertical Slices**: 13 feature domains
  - Orders
  - Invoices
  - Customers

## Metrics (optional)

**Total Types**: 234
  - Classes: 180
  - Interfaces: 45
  - Records: 6
  - Enums: 3

**Files**: 234 .cs files
**Lines of Code**: ~12,450

**Test Projects**: 2
**Test Files**: 89
```

## Storage Strategy
- **In-memory only**: No caching, no persistence
- Wiki is generated on-demand from existing Roslyn workspace
- Fast enough (<1s) since Roslyn already has solution loaded
- No cache invalidation needed - always fresh data

## Usage Examples

```bash
# Full wiki
get_codebase_wiki

# Focus on specific area
get_codebase_wiki focusArea="Application.UseCases"

# With metrics
get_codebase_wiki includeMetrics=true

# Minimal (structure only, no patterns)
get_codebase_wiki includePatterns=false
```

## Pattern Detection Logic

### Use Cases
Look for types implementing interfaces with "IUseCase" or "UseCase" in name.

### Result<T>
Count methods returning types with "Result" in name.

### Repositories
Types ending with "Repository" or implementing IRepository interfaces.
- Dapper: members containing "Query" or "Execute"
- EF Core: members containing "DbContext" or "DbSet"

### Vertical Slices
Extract domain from namespace pattern:
- `Application.{Domain}.{UseCase}` → Domain
- `Features.{Domain}.*` → Domain
- Fallback: second-to-last namespace segment

### Controllers/Endpoints
- Controllers: name ends with "Controller" or inherits from Controller base
- Endpoints: name ends with "Endpoint" or contains "Endpoints"

## Testing Checklist
- [ ] Tool generates wiki for full solution
- [ ] focusArea filters projects correctly
- [ ] focusArea filters namespaces correctly
- [ ] Pattern detection finds use cases
- [ ] Pattern detection finds repositories
- [ ] Pattern detection identifies Dapper vs EF Core
- [ ] Vertical slice extraction works for typical namespace patterns
- [ ] Metrics calculation includes all projects
- [ ] Output is compact (<500 lines for typical codebase)
- [ ] Tool works in Claude Code CLI session

## Integration Notes

**CLAUDE.md update**:
Add to project-level or root CLAUDE.md:
```markdown
## Codebase Navigation

Use `get_codebase_wiki` for instant structural overview:
- Project structure and namespace breakdown
- Architectural pattern analysis (use cases, repositories, slices)
- Optional metrics (file counts, LOC)

Combine with existing tools:
- `get_codebase_wiki` → structure overview
- `search_types` → find specific types
- `get_dependencies` → analyze relationships
```

**Workflow integration**:
- New worktree agent? Start with `get_codebase_wiki focusArea="Orders"`
- Architecture decision? Check patterns with `includePatterns=true`
- Code review? Get metrics with `includeMetrics=true`

## Non-Goals
- ❌ Persistent caching (in-memory only)
- ❌ File watching / live updates (on-demand generation)
- ❌ Detailed code analysis (that's for existing tools)
- ❌ Documentation generation (this is structural overview only)

## Success Criteria
- Claude Code CLI can call `get_codebase_wiki` and get instant overview
- Output is compact enough to fit in context alongside other prompts
- Pattern detection correctly identifies Datalake2 architecture
- Tool complements existing Roslyn analysis tools without duplication