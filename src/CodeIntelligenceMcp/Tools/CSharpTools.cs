namespace CodeIntelligenceMcp.Tools;

[McpServerToolType]
public sealed class CSharpTools(
    IWorkspaceProvider<RoslynWorkspaceIndex> roslynProvider,
    CleanArchRegistry cleanArch,
    SolutionPathRegistry solutionPaths,
    McpConfig config)
{
    private CleanArchitectureNames ResolveCleanArch(string workspace, RoslynWorkspaceIndex index)
    {
        CleanArchitectureNames configured = cleanArch.Config.GetValueOrDefault(workspace, new CleanArchitectureNames("", "", ""));
        return string.IsNullOrEmpty(configured.CoreProject) ? index.CleanArchitecture : configured;
    }

    // Simple names can match multiple types across namespaces; instead of silently picking
    // one, report all fully qualified candidates so the caller can disambiguate.
    private static string? AmbiguityError(RoslynWorkspaceIndex index, string typeName)
    {
        IReadOnlyList<string> candidates = index.GetTypeCandidates(typeName);
        if (candidates.Count <= 1)
            return null;

        return JsonSerializer.Serialize(new
        {
            error = $"ambiguous type name '{typeName}' — use a fully qualified name",
            candidates
        }, ToolResponses.JsonOptions);
    }

    [McpServerTool(Name = "get_type")]
    [Description("Get full structural details of a type: properties, methods, base type, interfaces. Use when you know the type name and need its members.")]
    public async Task<string> GetType(
        [Description("Workspace name from mcp-config.json, or absolute path to a .sln/.slnx for ad-hoc worktrees")] string workspace,
        [Description("Simple or fully qualified type name")] string typeName,
        CancellationToken ct = default)
    {
        (RoslynWorkspaceIndex? index, string? error) = await WorkspaceAccess.GetAsync(roslynProvider, config, "dotnet", workspace, ct);
        if (index is null)
            return error!;

        if (AmbiguityError(index, typeName) is string ambiguous)
            return ambiguous;

        TypeInfo? typeInfo = index.GetType(typeName);
        if (typeInfo is null)
            return ToolResponses.Err("type not found");

        return ToolResponses.Ok(typeInfo, index.IsStaleCached());
    }

    [McpServerTool(Name = "find_types")]
    [Description("Search for types by name, namespace, interface, attribute, or kind. Use to discover what exists in a domain without reading files.")]
    public async Task<string> FindTypes(
        [Description("Workspace name from mcp-config.json, or absolute path to a .sln/.slnx for ad-hoc worktrees")] string workspace,
        [Description("Type name filter: substring, or glob with * and ? (case-insensitive)")] string? nameContains = null,
        [Description("Exact or prefix namespace match")] string? @namespace = null,
        [Description("Interface the type must implement: simple name, or with type args like 'IUseCase<CreateRequest, Result>'")] string? implementsInterface = null,
        [Description("Attribute name the type must have")] string? hasAttribute = null,
        [Description("Type kind: class, interface, record, or enum")] string? kind = null,
        [Description("Maximum results to return (default 100, 0 = unlimited)")] int maxResults = 100,
        CancellationToken ct = default)
    {
        (RoslynWorkspaceIndex? index, string? error) = await WorkspaceAccess.GetAsync(roslynProvider, config, "dotnet", workspace, ct);
        if (index is null)
            return error!;

        IReadOnlyList<TypeSummary> results = index.FindTypes(nameContains, @namespace, implementsInterface, hasAttribute, kind);
        return ToolResponses.OkList(results, maxResults, index.IsStaleCached());
    }

    [McpServerTool(Name = "get_method")]
    [Description("Get the full source body of a specific method. Use when you need to read method logic without opening the file.")]
    public async Task<string> GetMethod(
        [Description("Workspace name from mcp-config.json, or absolute path to a .sln/.slnx for ad-hoc worktrees")] string workspace,
        [Description("Type name")] string typeName,
        [Description("Method name")] string methodName,
        CancellationToken ct = default)
    {
        (RoslynWorkspaceIndex? index, string? error) = await WorkspaceAccess.GetAsync(roslynProvider, config, "dotnet", workspace, ct);
        if (index is null)
            return error!;

        if (AmbiguityError(index, typeName) is string ambiguous)
            return ambiguous;

        MethodInfo? methodInfo = index.GetMethod(typeName, methodName);
        if (methodInfo is null)
            return ToolResponses.Err("method not found");

        return ToolResponses.Ok(methodInfo, index.IsStaleCached());
    }

    [McpServerTool(Name = "find_implementations")]
    [Description("Find all concrete types that implement a given interface. Use to map interfaces to their implementations.")]
    public async Task<string> FindImplementations(
        [Description("Workspace name from mcp-config.json, or absolute path to a .sln/.slnx for ad-hoc worktrees")] string workspace,
        [Description("Interface name: simple, or with type args like 'IUseCase<CreateRequest, Result>'")] string interfaceName,
        [Description("Maximum results to return (default 100, 0 = unlimited)")] int maxResults = 100,
        CancellationToken ct = default)
    {
        (RoslynWorkspaceIndex? index, string? error) = await WorkspaceAccess.GetAsync(roslynProvider, config, "dotnet", workspace, ct);
        if (index is null)
            return error!;

        IReadOnlyList<ImplementationSummary> results = index.FindImplementations(interfaceName);
        return ToolResponses.OkList(results, maxResults, index.IsStaleCached());
    }

    [McpServerTool(Name = "find_derived_types")]
    [Description("Find all types that derive from a given base class, at any depth. Complements find_implementations (which covers interfaces).")]
    public async Task<string> FindDerivedTypes(
        [Description("Workspace name from mcp-config.json, or absolute path to a .sln/.slnx for ad-hoc worktrees")] string workspace,
        [Description("Base class name (simple or fully qualified)")] string baseTypeName,
        [Description("Maximum results to return (default 100, 0 = unlimited)")] int maxResults = 100,
        CancellationToken ct = default)
    {
        (RoslynWorkspaceIndex? index, string? error) = await WorkspaceAccess.GetAsync(roslynProvider, config, "dotnet", workspace, ct);
        if (index is null)
            return error!;

        IReadOnlyList<ImplementationSummary> results = index.FindDerivedTypes(baseTypeName);
        return ToolResponses.OkList(results, maxResults, index.IsStaleCached());
    }

    [McpServerTool(Name = "find_usages")]
    [Description("Find all usages of a type, method, or field across the workspace. Use to understand impact before refactoring.")]
    public async Task<string> FindUsages(
        [Description("Workspace name from mcp-config.json, or absolute path to a .sln/.slnx for ad-hoc worktrees")] string workspace,
        [Description("Symbol name to find usages of")] string symbolName,
        [Description("Maximum results to return (default 100, 0 = unlimited)")] int maxResults = 100,
        CancellationToken ct = default)
    {
        (RoslynWorkspaceIndex? index, string? error) = await WorkspaceAccess.GetAsync(roslynProvider, config, "dotnet", workspace, ct);
        if (index is null)
            return error!;

        if (AmbiguityError(index, symbolName) is string ambiguous)
            return ambiguous;

        IReadOnlyList<UsageResult> results = await new ReferenceQueries(index).FindUsagesAsync(symbolName, ct);
        return ToolResponses.OkList(results, maxResults, index.IsStaleCached());
    }

    [McpServerTool(Name = "get_dependencies")]
    [Description("Get the constructor-injected dependencies of a type. Use to understand what a class depends on without reading its file.")]
    public async Task<string> GetDependencies(
        [Description("Workspace name from mcp-config.json, or absolute path to a .sln/.slnx for ad-hoc worktrees")] string workspace,
        [Description("Type name")] string typeName,
        CancellationToken ct = default)
    {
        (RoslynWorkspaceIndex? index, string? error) = await WorkspaceAccess.GetAsync(roslynProvider, config, "dotnet", workspace, ct);
        if (index is null)
            return error!;

        if (AmbiguityError(index, typeName) is string ambiguous)
            return ambiguous;

        DependencyInfo? depInfo = index.GetDependencies(typeName);
        if (depInfo is null)
            return ToolResponses.Err("type not found");

        return ToolResponses.Ok(depInfo, index.IsStaleCached());
    }

    [McpServerTool(Name = "get_public_surface")]
    [Description("List all public types in a namespace: interfaces, classes, records, enums. Use to understand what a layer exposes.")]
    public async Task<string> GetPublicSurface(
        [Description("Workspace name from mcp-config.json, or absolute path to a .sln/.slnx for ad-hoc worktrees")] string workspace,
        [Description("Namespace to inspect (exact or prefix)")] string @namespace,
        CancellationToken ct = default)
    {
        (RoslynWorkspaceIndex? index, string? error) = await WorkspaceAccess.GetAsync(roslynProvider, config, "dotnet", workspace, ct);
        if (index is null)
            return error!;

        PublicSurface surface = index.GetPublicSurface(@namespace);
        return ToolResponses.Ok(surface, index.IsStaleCached());
    }

    [McpServerTool(Name = "get_project_dependencies")]
    [Description("Get the project dependency graph: which projects reference which. Use to verify Clean Architecture layering.")]
    public async Task<string> GetProjectDependencies(
        [Description("Workspace name from mcp-config.json, or absolute path to a .sln/.slnx for ad-hoc worktrees")] string workspace,
        CancellationToken ct = default)
    {
        (RoslynWorkspaceIndex? index, string? error) = await WorkspaceAccess.GetAsync(roslynProvider, config, "dotnet", workspace, ct);
        if (index is null)
            return error!;

        ProjectDependency dep = index.GetProjectDependencies();
        return ToolResponses.Ok(dep, index.IsStaleCached());
    }

    [McpServerTool(Name = "search_symbol")]
    [Description("Search symbols (types, methods, properties) by substring or glob. Use when you know part of a name but not the full path. Compiler-generated members are excluded.")]
    public async Task<string> SearchSymbol(
        [Description("Workspace name from mcp-config.json, or absolute path to a .sln/.slnx for ad-hoc worktrees")] string workspace,
        [Description("Query: substring (case-insensitive), or glob with * and ? matched against the symbol name")] string query,
        [Description("Maximum results to return (default 100, 0 = unlimited)")] int maxResults = 100,
        CancellationToken ct = default)
    {
        (RoslynWorkspaceIndex? index, string? error) = await WorkspaceAccess.GetAsync(roslynProvider, config, "dotnet", workspace, ct);
        if (index is null)
            return error!;

        IReadOnlyList<SymbolSearchResult> results = index.SearchSymbol(query);
        return ToolResponses.OkList(results, maxResults, index.IsStaleCached());
    }

    [McpServerTool(Name = "scan_patterns")]
    [Description("Count types, interfaces, use cases, and razor components, and run all violation rules. Use as a quick structural health check.")]
    public async Task<string> ScanPatterns(
        [Description("Workspace name from mcp-config.json, or absolute path to a .sln/.slnx for ad-hoc worktrees")] string workspace,
        CancellationToken ct = default)
    {
        (RoslynWorkspaceIndex? index, string? error) = await WorkspaceAccess.GetAsync(roslynProvider, config, "dotnet", workspace, ct);
        if (index is null)
            return error!;

        CleanArchitectureNames ca = ResolveCleanArch(workspace, index);
        PatternScanner scanner = new(index, ca);
        PatternSummary summary = await scanner.ScanAsync(ct);
        return ToolResponses.Ok(summary, index.IsStaleCached());
    }

    [McpServerTool(Name = "get_test_coverage")]
    [Description("Report which use cases have a matching *Tests class in any .Tests project. Convention-based: use case name + 'Tests' must exist. Returns coverage percentage and lists uncovered use cases.")]
    public async Task<string> GetTestCoverage(
        [Description("Workspace name from mcp-config.json, or absolute path to a .sln/.slnx for ad-hoc worktrees")] string workspace,
        CancellationToken ct = default)
    {
        (RoslynWorkspaceIndex? index, string? error) = await WorkspaceAccess.GetAsync(roslynProvider, config, "dotnet", workspace, ct);
        if (index is null)
            return error!;

        return ToolResponses.Ok(index.GetTestCoverage(), index.IsStaleCached());
    }

    [McpServerTool(Name = "get_complexity")]
    [Description("List methods ordered by cyclomatic complexity or line count. Use to identify risky or hard-to-test code. Complexity >= 8 warrants review; >= 15 is a refactor candidate.")]
    public async Task<string> GetComplexity(
        [Description("Workspace name from mcp-config.json, or absolute path to a .sln/.slnx for ad-hoc worktrees")] string workspace,
        [Description("Only return methods with complexity >= this threshold (default 5)")] int minComplexity = 5,
        [Description("Filter to a specific project name (substring match)")] string? projectFilter = null,
        [Description("Also include methods with >= this many lines regardless of complexity (default 0 = disabled)")] int minLines = 0,
        [Description("Sort by 'complexity' (default) or 'lines'")] string sortBy = "complexity",
        [Description("Maximum results to return (default 100, 0 = unlimited)")] int maxResults = 100,
        CancellationToken ct = default)
    {
        (RoslynWorkspaceIndex? index, string? error) = await WorkspaceAccess.GetAsync(roslynProvider, config, "dotnet", workspace, ct);
        if (index is null)
            return error!;

        ComplexityAnalyzer analyzer = new(index);
        IReadOnlyList<MethodComplexity> results = await analyzer.AnalyzeAsync(minComplexity, projectFilter, minLines, sortBy, ct: ct);
        return ToolResponses.OkList(results, maxResults, index.IsStaleCached());
    }

    [McpServerTool(Name = "scan_all_violations")]
    [Description("Run every violation rule in one call. Returns only rules with violations, ordered by count descending. Use as a quick codebase health check at the start of a session.")]
    public async Task<string> ScanAllViolations(
        [Description("Workspace name from mcp-config.json, or absolute path to a .sln/.slnx for ad-hoc worktrees")] string workspace,
        [Description("Maximum violations to include per rule (default 50, 0 = unlimited)")] int maxPerRule = 50,
        CancellationToken ct = default)
    {
        (RoslynWorkspaceIndex? index, string? error) = await WorkspaceAccess.GetAsync(roslynProvider, config, "dotnet", workspace, ct);
        if (index is null)
            return error!;

        CleanArchitectureNames ca = ResolveCleanArch(workspace, index);
        ViolationDetector detector = new(index, ca);

        List<(string Rule, int Count, IReadOnlyList<ViolationResult> Violations)> results = [];

        foreach (string rule in ViolationDetector.AllRuleKeys)
        {
            try
            {
                IReadOnlyList<ViolationResult> violations = await detector.DetectAsync(rule, ct);
                if (violations.Count == 0)
                    continue;

                IReadOnlyList<ViolationResult> capped = maxPerRule > 0 && violations.Count > maxPerRule
                    ? [.. violations.Take(maxPerRule)]
                    : violations;
                results.Add((rule, violations.Count, capped));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // Rule unsupported for this workspace config — skip
            }
        }

        return ToolResponses.Ok(results
            .OrderByDescending(r => r.Count)
            .Select(r => new { rule = r.Rule, count = r.Count, violations = r.Violations })
            .ToList(), index.IsStaleCached());
    }

    [McpServerTool(Name = "find_dead_code")]
    [Description("Find private methods, properties, and fields that have no references. Scoped to private members only. Use projectFilter to narrow scope.")]
    public async Task<string> FindDeadCode(
        [Description("Workspace name from mcp-config.json, or absolute path to a .sln/.slnx for ad-hoc worktrees")] string workspace,
        [Description("Filter to a specific project name (substring match)")] string? projectFilter = null,
        [Description("Maximum results to return (default 100, 0 = unlimited)")] int maxResults = 100,
        CancellationToken ct = default)
    {
        (RoslynWorkspaceIndex? index, string? error) = await WorkspaceAccess.GetAsync(roslynProvider, config, "dotnet", workspace, ct);
        if (index is null)
            return error!;

        IReadOnlyList<DeadCodeResult> results = await new ReferenceQueries(index).FindDeadCodeAsync(projectFilter, ct);
        return ToolResponses.OkList(results, maxResults, index.IsStaleCached());
    }

    [McpServerTool(Name = "find_callers")]
    [Description("Find all callers of a specific method. Use before refactoring to understand impact — returns caller type, method name, file, line, and the calling line text.")]
    public async Task<string> FindCallers(
        [Description("Workspace name from mcp-config.json, or absolute path to a .sln/.slnx for ad-hoc worktrees")] string workspace,
        [Description("Type name that owns the method")] string typeName,
        [Description("Method name to find callers of")] string methodName,
        [Description("Maximum results to return (default 100, 0 = unlimited)")] int maxResults = 100,
        CancellationToken ct = default)
    {
        (RoslynWorkspaceIndex? index, string? error) = await WorkspaceAccess.GetAsync(roslynProvider, config, "dotnet", workspace, ct);
        if (index is null)
            return error!;

        if (AmbiguityError(index, typeName) is string ambiguous)
            return ambiguous;

        IReadOnlyList<CallerResult> results = await new ReferenceQueries(index).FindCallersAsync(typeName, methodName, ct);
        return ToolResponses.OkList(results, maxResults, index.IsStaleCached());
    }

    [McpServerTool(Name = "get_coupling")]
    [Description("List types ordered by efferent coupling (number of unique external types they depend on). Combine with get_complexity to find the highest-risk refactoring candidates.")]
    public async Task<string> GetCoupling(
        [Description("Workspace name from mcp-config.json, or absolute path to a .sln/.slnx for ad-hoc worktrees")] string workspace,
        [Description("Only return types with coupling >= this threshold (default 5)")] int minCoupling = 5,
        [Description("Filter to a specific project name (substring match)")] string? projectFilter = null,
        [Description("Maximum results to return (default 100, 0 = unlimited)")] int maxResults = 100,
        CancellationToken ct = default)
    {
        (RoslynWorkspaceIndex? index, string? error) = await WorkspaceAccess.GetAsync(roslynProvider, config, "dotnet", workspace, ct);
        if (index is null)
            return error!;

        IReadOnlyList<TypeCoupling> results = new CouplingAnalyzer(index).GetCoupling(projectFilter, minCoupling);
        return ToolResponses.OkList(results, maxResults, index.IsStaleCached());
    }

    [McpServerTool(Name = "get_hotspots")]
    [Description("List the top N types with the highest combined risk score (coupling + complexity + missing tests). Use at the start of a session to find the most dangerous code to touch — no type name needed.")]
    public async Task<string> GetHotspots(
        [Description("Workspace name from mcp-config.json, or absolute path to a .sln/.slnx for ad-hoc worktrees")] string workspace,
        [Description("Maximum number of results to return (default 20)")] int topN = 20,
        [Description("Filter to a specific project name (substring match)")] string? projectFilter = null,
        CancellationToken ct = default)
    {
        (RoslynWorkspaceIndex? index, string? error) = await WorkspaceAccess.GetAsync(roslynProvider, config, "dotnet", workspace, ct);
        if (index is null)
            return error!;

        return ToolResponses.Ok(await new RiskAnalyzer(index).GetHotspotsAsync(topN, projectFilter, ct), index.IsStaleCached());
    }

    [McpServerTool(Name = "find_circular_dependencies")]
    [Description("Find cycles in the project dependency graph. Circular dependencies prevent clean layering and block independent deployment. Returns each cycle as an ordered list of project names.")]
    public async Task<string> FindCircularDependencies(
        [Description("Workspace name from mcp-config.json, or absolute path to a .sln/.slnx for ad-hoc worktrees")] string workspace,
        CancellationToken ct = default)
    {
        (RoslynWorkspaceIndex? index, string? error) = await WorkspaceAccess.GetAsync(roslynProvider, config, "dotnet", workspace, ct);
        if (index is null)
            return error!;

        IReadOnlyList<IReadOnlyList<string>> cycles = index.FindCircularDependencies();
        return ToolResponses.Ok(new { cycleCount = cycles.Count, cycles }, index.IsStaleCached());
    }

    [McpServerTool(Name = "get_change_risk")]
    [Description("Score the refactoring risk for a type: 0-100 based on referencing types, coupling, max complexity, and test coverage. Use before refactoring to understand blast radius.")]
    public async Task<string> GetChangeRisk(
        [Description("Workspace name from mcp-config.json, or absolute path to a .sln/.slnx for ad-hoc worktrees")] string workspace,
        [Description("Type name to assess")] string typeName,
        CancellationToken ct = default)
    {
        (RoslynWorkspaceIndex? index, string? error) = await WorkspaceAccess.GetAsync(roslynProvider, config, "dotnet", workspace, ct);
        if (index is null)
            return error!;

        if (AmbiguityError(index, typeName) is string ambiguous)
            return ambiguous;

        ChangeRiskResult? result = await new RiskAnalyzer(index).GetChangeRiskAsync(typeName, ct);
        if (result is null)
            return ToolResponses.Err("type not found");

        return ToolResponses.Ok(result, index.IsStaleCached());
    }

    [McpServerTool(Name = "find_violations")]
    [Description("Run a specific architectural rule across the workspace. Rules: core-no-ef, core-no-http, core-no-azure, usecase-not-sealed, inline-viewmodel-razor, business-logic-in-razor, json-parsing-in-view, blazor-injects-infra, controller-not-thin, dto-in-core, missing-cancellation-token, no-async-void, async-over-sync, use-case-not-thin, empty-catch, throw-ex, layer-boundary, too-many-params, services-in-web, missing-interface, direct-instantiation.")]
    public async Task<string> FindViolations(
        [Description("Workspace name from mcp-config.json, or absolute path to a .sln/.slnx for ad-hoc worktrees")] string workspace,
        [Description("Rule key (see tool description for the full list)")] string rule,
        [Description("Filter results to a specific project name (substring match on file path)")] string? projectFilter = null,
        [Description("Maximum results to return (default 100, 0 = unlimited)")] int maxResults = 100,
        CancellationToken ct = default)
    {
        (RoslynWorkspaceIndex? index, string? error) = await WorkspaceAccess.GetAsync(roslynProvider, config, "dotnet", workspace, ct);
        if (index is null)
            return error!;

        CleanArchitectureNames ca = ResolveCleanArch(workspace, index);
        ViolationDetector detector = new(index, ca);

        try
        {
            IReadOnlyList<ViolationResult> violations = await detector.DetectAsync(rule, ct);

            if (projectFilter is not null)
            {
                violations = [.. violations.Where(v =>
                    v.FilePath.Contains(projectFilter, StringComparison.OrdinalIgnoreCase)
                    || (v.TypeName?.Contains(projectFilter, StringComparison.OrdinalIgnoreCase) == true))];
            }

            return ToolResponses.OkList(violations, maxResults, index.IsStaleCached());
        }
        catch (ArgumentException ex)
        {
            return ToolResponses.Err(ex.Message);
        }
    }

    [McpServerTool(Name = "analyze_file")]
    [Description("Analyze a single .cs or .razor file for structural observations: missing CancellationToken, layer violations, inline types, JSON in view. Use when you need file-level detail after scan_patterns signals an issue.")]
    public async Task<string> AnalyzeFile(
        [Description("Workspace name from mcp-config.json, or absolute path to a .sln/.slnx for ad-hoc worktrees")] string workspace,
        [Description("File path (relative to solution root or absolute)")] string filePath,
        CancellationToken ct = default)
    {
        (RoslynWorkspaceIndex? index, string? error) = await WorkspaceAccess.GetAsync(roslynProvider, config, "dotnet", workspace, ct);
        if (index is null)
            return error!;

        string solutionPath = solutionPaths.Paths.GetValueOrDefault(workspace, "");
        string solutionDir = Path.GetDirectoryName(solutionPath) ?? "";

        string fullPath = Path.IsPathRooted(filePath)
            ? filePath
            : Path.GetFullPath(Path.Combine(solutionDir, filePath));

        if (!File.Exists(fullPath))
            return ToolResponses.Err($"file not found: {fullPath}");

        CleanArchitectureNames ca = ResolveCleanArch(workspace, index);
        FileAnalysis analysis = FileAnalyzer.Analyze(fullPath, ca);
        return ToolResponses.Ok(analysis, index.IsStaleCached());
    }
}
