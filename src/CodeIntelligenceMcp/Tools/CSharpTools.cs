namespace CodeIntelligenceMcp.Tools;

[McpServerToolType]
public sealed class CSharpTools(
    IWorkspaceProvider<RoslynWorkspaceIndex> roslynProvider,
    CleanArchRegistry cleanArch,
    SolutionPathRegistry solutionPaths)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static string Ok(object result) => JsonSerializer.Serialize(result, JsonOptions);
    private static string Err(string message) => JsonSerializer.Serialize(new { error = message });

    private CleanArchitectureNames ResolveCleanArch(string workspace, RoslynWorkspaceIndex index)
    {
        CleanArchitectureNames configured = cleanArch.Config.GetValueOrDefault(workspace, new CleanArchitectureNames("", "", ""));
        return string.IsNullOrEmpty(configured.CoreProject) ? index.CleanArchitecture : configured;
    }

    [McpServerTool(Name = "get_type")]
    [Description("Get full structural details of a type: properties, methods, base type, interfaces. Use when you know the type name and need its members.")]
    public async Task<string> GetType(
        [Description("Workspace name from mcp-config.json, or absolute path to a .sln/.slnx for ad-hoc worktrees")] string workspace,
        [Description("Simple or fully qualified type name")] string typeName,
        CancellationToken ct = default)
    {
        RoslynWorkspaceIndex? index = await roslynProvider.GetAsync(workspace, ct);
        if (index is null)
            return Err($"workspace '{workspace}' not found");

        TypeInfo? typeInfo = index.GetType(typeName);
        if (typeInfo is null)
            return Err("type not found");

        return Ok(typeInfo);
    }

    [McpServerTool(Name = "find_types")]
    [Description("Search for types by name, namespace, interface, attribute, or kind. Use to discover what exists in a domain without reading files.")]
    public async Task<string> FindTypes(
        [Description("Workspace name from mcp-config.json, or absolute path to a .sln/.slnx for ad-hoc worktrees")] string workspace,
        [Description("Substring match on type name (case-insensitive)")] string? nameContains = null,
        [Description("Exact or prefix namespace match")] string? @namespace = null,
        [Description("Interface name the type must implement")] string? implementsInterface = null,
        [Description("Attribute name the type must have")] string? hasAttribute = null,
        [Description("Type kind: class, interface, record, or enum")] string? kind = null,
        CancellationToken ct = default)
    {
        RoslynWorkspaceIndex? index = await roslynProvider.GetAsync(workspace, ct);
        if (index is null)
            return Err($"workspace '{workspace}' not found");

        IReadOnlyList<TypeSummary> results = index.FindTypes(nameContains, @namespace, implementsInterface, hasAttribute, kind);
        return Ok(results);
    }

    [McpServerTool(Name = "get_method")]
    [Description("Get the full source body of a specific method. Use when you need to read method logic without opening the file.")]
    public async Task<string> GetMethod(
        [Description("Workspace name from mcp-config.json, or absolute path to a .sln/.slnx for ad-hoc worktrees")] string workspace,
        [Description("Type name")] string typeName,
        [Description("Method name")] string methodName,
        CancellationToken ct = default)
    {
        RoslynWorkspaceIndex? index = await roslynProvider.GetAsync(workspace, ct);
        if (index is null)
            return Err($"workspace '{workspace}' not found");

        MethodInfo? methodInfo = index.GetMethod(typeName, methodName);
        if (methodInfo is null)
            return Err("method not found");

        return Ok(methodInfo);
    }

    [McpServerTool(Name = "find_implementations")]
    [Description("Find all concrete types that implement a given interface. Use to map interfaces to their implementations.")]
    public async Task<string> FindImplementations(
        [Description("Workspace name from mcp-config.json, or absolute path to a .sln/.slnx for ad-hoc worktrees")] string workspace,
        [Description("Interface name")] string interfaceName,
        CancellationToken ct = default)
    {
        RoslynWorkspaceIndex? index = await roslynProvider.GetAsync(workspace, ct);
        if (index is null)
            return Err($"workspace '{workspace}' not found");

        IReadOnlyList<ImplementationSummary> results = index.FindImplementations(interfaceName);
        return Ok(results);
    }

    [McpServerTool(Name = "find_usages")]
    [Description("Find all usages of a type, method, or field across the workspace. Use to understand impact before refactoring.")]
    public async Task<string> FindUsages(
        [Description("Workspace name from mcp-config.json, or absolute path to a .sln/.slnx for ad-hoc worktrees")] string workspace,
        [Description("Symbol name to find usages of")] string symbolName,
        CancellationToken ct = default)
    {
        RoslynWorkspaceIndex? index = await roslynProvider.GetAsync(workspace, ct);
        if (index is null)
            return Err($"workspace '{workspace}' not found");

        IReadOnlyList<UsageResult> results = await index.FindUsagesAsync(symbolName, ct);
        return Ok(results);
    }

    [McpServerTool(Name = "get_dependencies")]
    [Description("Get the constructor-injected dependencies of a type. Use to understand what a class depends on without reading its file.")]
    public async Task<string> GetDependencies(
        [Description("Workspace name from mcp-config.json, or absolute path to a .sln/.slnx for ad-hoc worktrees")] string workspace,
        [Description("Type name")] string typeName,
        CancellationToken ct = default)
    {
        RoslynWorkspaceIndex? index = await roslynProvider.GetAsync(workspace, ct);
        if (index is null)
            return Err($"workspace '{workspace}' not found");

        DependencyInfo? depInfo = index.GetDependencies(typeName);
        if (depInfo is null)
            return Err("type not found");

        return Ok(depInfo);
    }

    [McpServerTool(Name = "get_public_surface")]
    [Description("List all public types in a namespace: interfaces, classes, records, enums. Use to understand what a layer exposes.")]
    public async Task<string> GetPublicSurface(
        [Description("Workspace name from mcp-config.json, or absolute path to a .sln/.slnx for ad-hoc worktrees")] string workspace,
        [Description("Namespace to inspect (exact or prefix)")] string @namespace,
        CancellationToken ct = default)
    {
        RoslynWorkspaceIndex? index = await roslynProvider.GetAsync(workspace, ct);
        if (index is null)
            return Err($"workspace '{workspace}' not found");

        PublicSurface surface = index.GetPublicSurface(@namespace);
        return Ok(surface);
    }

    [McpServerTool(Name = "get_project_dependencies")]
    [Description("Get the project dependency graph: which projects reference which. Use to verify Clean Architecture layering.")]
    public async Task<string> GetProjectDependencies(
        [Description("Workspace name from mcp-config.json, or absolute path to a .sln/.slnx for ad-hoc worktrees")] string workspace,
        CancellationToken ct = default)
    {
        RoslynWorkspaceIndex? index = await roslynProvider.GetAsync(workspace, ct);
        if (index is null)
            return Err($"workspace '{workspace}' not found");

        ProjectDependency dep = index.GetProjectDependencies();
        return Ok(dep);
    }

    [McpServerTool(Name = "search_symbol")]
    [Description("Search symbols (types, methods, properties) by substring. Use when you know part of a name but not the full path.")]
    public async Task<string> SearchSymbol(
        [Description("Workspace name from mcp-config.json, or absolute path to a .sln/.slnx for ad-hoc worktrees")] string workspace,
        [Description("Substring query (case-insensitive)")] string query,
        CancellationToken ct = default)
    {
        RoslynWorkspaceIndex? index = await roslynProvider.GetAsync(workspace, ct);
        if (index is null)
            return Err($"workspace '{workspace}' not found");

        IReadOnlyList<SymbolSearchResult> results = index.SearchSymbol(query);
        return Ok(results);
    }

    [McpServerTool(Name = "scan_patterns")]
    [Description("Count types, interfaces, use cases, and razor components, and run all violation rules. Use as a quick structural health check.")]
    public async Task<string> ScanPatterns(
        [Description("Workspace name from mcp-config.json, or absolute path to a .sln/.slnx for ad-hoc worktrees")] string workspace,
        CancellationToken ct = default)
    {
        RoslynWorkspaceIndex? index = await roslynProvider.GetAsync(workspace, ct);
        if (index is null)
            return Err($"workspace '{workspace}' not found");

        CleanArchitectureNames ca = ResolveCleanArch(workspace, index);
        PatternScanner scanner = new(index, ca);
        PatternSummary summary = scanner.Scan();
        return Ok(summary);
    }

    [McpServerTool(Name = "find_violations")]
    [Description("Run a specific architectural rule across the workspace. Rules: core-no-ef, core-no-http, core-no-azure, usecase-not-sealed, inline-viewmodel-razor, business-logic-in-razor, json-parsing-in-view, controller-not-thin, dto-in-core.")]
    public async Task<string> FindViolations(
        [Description("Workspace name from mcp-config.json, or absolute path to a .sln/.slnx for ad-hoc worktrees")] string workspace,
        [Description("Rule key: core-no-ef, core-no-http, core-no-azure, usecase-not-sealed, inline-viewmodel-razor, business-logic-in-razor, json-parsing-in-view, controller-not-thin, dto-in-core")] string rule,
        CancellationToken ct = default)
    {
        RoslynWorkspaceIndex? index = await roslynProvider.GetAsync(workspace, ct);
        if (index is null)
            return Err($"workspace '{workspace}' not found");

        CleanArchitectureNames ca = ResolveCleanArch(workspace, index);
        ViolationDetector detector = new(index, ca);

        try
        {
            IReadOnlyList<ViolationResult> violations = detector.Detect(rule);
            return Ok(violations);
        }
        catch (ArgumentException ex)
        {
            return Err(ex.Message);
        }
    }

    [McpServerTool(Name = "analyze_file")]
    [Description("Analyze a single .cs or .razor file for structural observations: missing CancellationToken, layer violations, inline types, JSON in view. Use when you need file-level detail after scan_patterns signals an issue.")]
    public async Task<string> AnalyzeFile(
        [Description("Workspace name from mcp-config.json, or absolute path to a .sln/.slnx for ad-hoc worktrees")] string workspace,
        [Description("File path (relative to solution root or absolute)")] string filePath,
        CancellationToken ct = default)
    {
        RoslynWorkspaceIndex? index = await roslynProvider.GetAsync(workspace, ct);
        if (index is null)
            return Err($"workspace '{workspace}' not found");

        string solutionPath = solutionPaths.Paths.GetValueOrDefault(workspace, "");
        string solutionDir = Path.GetDirectoryName(solutionPath) ?? "";

        string fullPath = Path.IsPathRooted(filePath)
            ? filePath
            : Path.GetFullPath(Path.Combine(solutionDir, filePath));

        string extension = Path.GetExtension(fullPath).ToLowerInvariant();
        string fileType = extension == ".razor" ? "razor" : "cs";

        List<object> observations = [];

        if (fileType == "razor")
        {
            BlazorCodeBlock? codeBlock = BlazorFilePreprocessor.ExtractCodeBlock(fullPath);
            if (codeBlock is null)
                return Ok(new { filePath, fileType, observations = Array.Empty<object>() });

            Microsoft.CodeAnalysis.SyntaxTree tree = CSharpSyntaxTree.ParseText(codeBlock.Source);
            Microsoft.CodeAnalysis.SyntaxNode root = tree.GetRoot();

            foreach (ClassDeclarationSyntax classDecl in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                string name = classDecl.Identifier.Text;
                observations.Add(new
                {
                    kind = "inline-type",
                    location = name,
                    detail = $"Class '{name}' defined inline in @code block"
                });
            }

            foreach (InvocationExpressionSyntax invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                string expr = invocation.Expression.ToString();

                if (expr.Contains("UseCase", StringComparison.OrdinalIgnoreCase))
                {
                    int lineInBlock = invocation.GetLocation().GetLineSpan().StartLinePosition.Line;
                    int line = codeBlock.LineOffset + lineInBlock;
                    observations.Add(new
                    {
                        kind = "business-logic-in-view",
                        location = $"line {line}",
                        detail = $"Use case invoked directly: {expr}"
                    });
                    continue;
                }

                string[] projectionMethods = ["SelectMany", "GroupBy", "ToDictionary"];
                string? matchedMethod = projectionMethods.FirstOrDefault(m =>
                    expr.EndsWith("." + m, StringComparison.OrdinalIgnoreCase)
                    || expr.Equals(m, StringComparison.OrdinalIgnoreCase));

                if (matchedMethod is not null)
                {
                    int lineInBlock = invocation.GetLocation().GetLineSpan().StartLinePosition.Line;
                    int line = codeBlock.LineOffset + lineInBlock;
                    observations.Add(new
                    {
                        kind = "data-assembly-in-component",
                        location = $"line {line}",
                        detail = $"LINQ projection '{matchedMethod}' in component"
                    });
                }
            }

            string[] jsonIdentifiers = ["JsonDocument", "JsonSerializer"];
            foreach (IdentifierNameSyntax identifier in root.DescendantNodes().OfType<IdentifierNameSyntax>())
            {
                string name = identifier.Identifier.Text;
                if (!jsonIdentifiers.Contains(name, StringComparer.Ordinal))
                    continue;

                int lineInBlock = identifier.GetLocation().GetLineSpan().StartLinePosition.Line;
                int line = codeBlock.LineOffset + lineInBlock;
                observations.Add(new
                {
                    kind = "json-parsing-in-view",
                    location = $"line {line}",
                    detail = $"'{name}' used in component"
                });
            }

            foreach (MethodDeclarationSyntax method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                bool isPublic = method.Modifiers.Any(m => m.Text == "public");
                bool isAsync = method.Modifiers.Any(m => m.Text == "async");
                if (!isPublic || !isAsync)
                    continue;

                bool hasCancellationToken = method.ParameterList.Parameters.Any(p =>
                    p.Type?.ToString().Contains("CancellationToken", StringComparison.Ordinal) == true);

                if (!hasCancellationToken)
                {
                    string methodName = method.Identifier.Text;
                    observations.Add(new
                    {
                        kind = "missing-cancellation-token",
                        location = methodName,
                        detail = $"Public async method '{methodName}' has no CancellationToken parameter"
                    });
                }
            }
        }
        else
        {
            if (!File.Exists(fullPath))
                return Err($"file not found: {fullPath}");

            string source = File.ReadAllText(fullPath);
            Microsoft.CodeAnalysis.SyntaxTree tree = CSharpSyntaxTree.ParseText(source);
            Microsoft.CodeAnalysis.SyntaxNode root = tree.GetRoot();

            foreach (MethodDeclarationSyntax method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                bool isPublic = method.Modifiers.Any(m => m.Text == "public");
                bool isAsync = method.Modifiers.Any(m => m.Text == "async");
                if (!isPublic || !isAsync)
                    continue;

                bool hasCancellationToken = method.ParameterList.Parameters.Any(p =>
                    p.Type?.ToString().Contains("CancellationToken", StringComparison.Ordinal) == true);

                if (!hasCancellationToken)
                {
                    string methodName = method.Identifier.Text;
                    observations.Add(new
                    {
                        kind = "missing-cancellation-token",
                        location = methodName,
                        detail = $"Public async method '{methodName}' has no CancellationToken parameter"
                    });
                }
            }

            CleanArchitectureNames ca = ResolveCleanArch(workspace, index);

            if (!string.IsNullOrEmpty(ca.CoreProject))
            {
                string normalizedPath = fullPath.Replace('\\', '/');

                string? layer = null;
                if (normalizedPath.Contains("/" + ca.CoreProject + "/", StringComparison.OrdinalIgnoreCase)
                    || normalizedPath.Contains("\\" + ca.CoreProject + "\\", StringComparison.OrdinalIgnoreCase))
                    layer = "core";
                else if (normalizedPath.Contains("/" + ca.InfraProject + "/", StringComparison.OrdinalIgnoreCase)
                    || normalizedPath.Contains("\\" + ca.InfraProject + "\\", StringComparison.OrdinalIgnoreCase))
                    layer = "infrastructure";
                else if (normalizedPath.Contains("/" + ca.WebProject + "/", StringComparison.OrdinalIgnoreCase)
                    || normalizedPath.Contains("\\" + ca.WebProject + "\\", StringComparison.OrdinalIgnoreCase))
                    layer = "web";

                if (layer == "core")
                {
                    foreach (UsingDirectiveSyntax usingDirective in root.DescendantNodes().OfType<UsingDirectiveSyntax>())
                    {
                        string ns = usingDirective.Name?.ToString() ?? string.Empty;

                        if (ns.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.OrdinalIgnoreCase))
                        {
                            int lineNumber = usingDirective.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                            observations.Add(new
                            {
                                kind = "layer-violation",
                                location = $"line {lineNumber}",
                                detail = $"Core project must not reference EF Core. Found: using {ns}"
                            });
                        }
                        else if (ns.Equals("System.Net.Http", StringComparison.OrdinalIgnoreCase)
                            || ns.Contains("IHttpClientFactory", StringComparison.OrdinalIgnoreCase))
                        {
                            int lineNumber = usingDirective.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                            observations.Add(new
                            {
                                kind = "layer-violation",
                                location = $"line {lineNumber}",
                                detail = $"Core project must not reference HTTP types. Found: using {ns}"
                            });
                        }
                        else if (ns.StartsWith("Azure.", StringComparison.OrdinalIgnoreCase)
                            || ns.StartsWith("Microsoft.Azure.", StringComparison.OrdinalIgnoreCase))
                        {
                            int lineNumber = usingDirective.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                            observations.Add(new
                            {
                                kind = "layer-violation",
                                location = $"line {lineNumber}",
                                detail = $"Core project must not reference Azure SDK. Found: using {ns}"
                            });
                        }
                    }
                }
            }
        }

        return Ok(new { filePath, fileType, observations });
    }
}
