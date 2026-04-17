using CodeIntelligenceMcp.Roslyn.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.MSBuild;

namespace CodeIntelligenceMcp.Roslyn;

public sealed class RoslynWorkspaceIndex : IDisposable
{
    private readonly MSBuildWorkspace? _workspace;
    private readonly Solution? _solution;
    private readonly CleanArchitectureNames _cleanArch;
    private readonly IReadOnlyList<IndexedType> _allTypes;
    private readonly IReadOnlyDictionary<string, IndexedType> _typeByFqn;
    private readonly ILookup<string, IndexedType> _typeBySimpleName;

    private record IndexedType(
        INamedTypeSymbol Symbol,
        Compilation Compilation,
        string ProjectName,
        string FilePath,
        int LineStart);

    private RoslynWorkspaceIndex(
        MSBuildWorkspace? workspace,
        Solution? solution,
        CleanArchitectureNames cleanArch,
        IReadOnlyList<IndexedType> allTypes,
        IReadOnlyDictionary<string, IndexedType> typeByFqn,
        ILookup<string, IndexedType> typeBySimpleName)
    {
        _workspace = workspace;
        _solution = solution;
        _cleanArch = cleanArch;
        _allTypes = allTypes;
        _typeByFqn = typeByFqn;
        _typeBySimpleName = typeBySimpleName;
    }

    public int TypeCount => _allTypes.Count;
    public CleanArchitectureNames CleanArchitecture => _cleanArch;

    // Creates an index from in-memory compilations for unit testing.
    // GetProjectDocuments, GetRazorDocuments, and FindUsagesAsync are not available in this mode.
    internal static RoslynWorkspaceIndex CreateForTesting(
        IEnumerable<(Compilation Compilation, string ProjectName)> compilations,
        CleanArchitectureNames cleanArch)
    {
        List<IndexedType> allTypes = [];

        foreach ((Compilation compilation, string projectName) in compilations)
        {
            foreach (INamedTypeSymbol type in GetAllTypes(compilation.GlobalNamespace))
            {
                Location? location = type.Locations.FirstOrDefault(l => l.IsInSource);

                if (location is null)
                    continue;

                string filePath = location.SourceTree?.FilePath ?? string.Empty;
                int lineStart = location.GetLineSpan().StartLinePosition.Line + 1;

                allTypes.Add(new IndexedType(type, compilation, projectName, filePath, lineStart));
            }
        }

        Dictionary<string, IndexedType> typeByFqn = new(StringComparer.Ordinal);
        foreach (IndexedType indexed in allTypes)
            typeByFqn.TryAdd(indexed.Symbol.ToDisplayString(), indexed);

        ILookup<string, IndexedType> typeBySimpleName = allTypes.ToLookup(
            t => t.Symbol.Name,
            StringComparer.OrdinalIgnoreCase);

        return new RoslynWorkspaceIndex(null, null, cleanArch, allTypes, typeByFqn, typeBySimpleName);
    }

    public static async Task<RoslynWorkspaceIndex> BuildAsync(
        MSBuildWorkspace workspace,
        Solution solution,
        CleanArchitectureNames cleanArch,
        CancellationToken cancellationToken = default)
    {
        List<IndexedType> allTypes = [];

        foreach (Project project in solution.Projects)
        {
            Compilation? compilation = await project.GetCompilationAsync(cancellationToken);
            if (compilation is null)
                continue;

            foreach (INamedTypeSymbol type in GetAllTypes(compilation.Assembly.GlobalNamespace))
            {
                Location? location = type.Locations.FirstOrDefault(l => l.IsInSource);

                if (location is null)
                    continue;

                string filePath = location.SourceTree?.FilePath ?? string.Empty;

                if (filePath.EndsWith(".razor.g.cs", StringComparison.OrdinalIgnoreCase))
                    filePath = filePath[..^".g.cs".Length];

                int lineStart = location.GetLineSpan().StartLinePosition.Line + 1;

                allTypes.Add(new IndexedType(type, compilation, project.Name, filePath, lineStart));
            }
        }

        Dictionary<string, IndexedType> typeByFqn = new(StringComparer.Ordinal);
        foreach (IndexedType indexed in allTypes)
        {
            string fqn = indexed.Symbol.ToDisplayString();
            typeByFqn.TryAdd(fqn, indexed);
        }

        ILookup<string, IndexedType> typeBySimpleName = allTypes.ToLookup(
            t => t.Symbol.Name,
            StringComparer.OrdinalIgnoreCase);

        CleanArchitectureNames effectiveCleanArch =
            string.IsNullOrEmpty(cleanArch.CoreProject)
                ? AutoDetectCleanArchitecture(solution)
                : cleanArch;

        return new RoslynWorkspaceIndex(workspace, solution, effectiveCleanArch, allTypes, typeByFqn, typeBySimpleName);
    }

    private static CleanArchitectureNames AutoDetectCleanArchitecture(Solution solution)
    {
        IReadOnlyList<string> names = solution.Projects
            .Where(p => !p.Name.Contains("Test", StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Name)
            .ToList();

        string core = names.FirstOrDefault(n =>
            n.EndsWith(".Core", StringComparison.OrdinalIgnoreCase)) ?? string.Empty;

        string infra = names.FirstOrDefault(n =>
            n.EndsWith(".Infrastructure", StringComparison.OrdinalIgnoreCase)
            || n.EndsWith(".Infra", StringComparison.OrdinalIgnoreCase)
            || n.EndsWith(".Data", StringComparison.OrdinalIgnoreCase)
            || n.EndsWith(".Persistence", StringComparison.OrdinalIgnoreCase)) ?? string.Empty;

        string web = names.FirstOrDefault(n =>
            n.EndsWith(".Api", StringComparison.OrdinalIgnoreCase)
            || n.EndsWith(".Web", StringComparison.OrdinalIgnoreCase)
            || n.EndsWith(".Mvc", StringComparison.OrdinalIgnoreCase)) ?? string.Empty;

        return new CleanArchitectureNames(core, infra, web);
    }

    private static IEnumerable<INamedTypeSymbol> GetAllTypes(INamespaceSymbol ns)
    {
        foreach (INamedTypeSymbol type in ns.GetTypeMembers())
        {
            yield return type;
            foreach (INamedTypeSymbol nested in GetNestedTypes(type))
                yield return nested;
        }

        foreach (INamespaceSymbol childNs in ns.GetNamespaceMembers())
        {
            foreach (INamedTypeSymbol type in GetAllTypes(childNs))
                yield return type;
        }
    }

    private static IEnumerable<INamedTypeSymbol> GetNestedTypes(INamedTypeSymbol type)
    {
        foreach (INamedTypeSymbol nested in type.GetTypeMembers())
        {
            yield return nested;
            foreach (INamedTypeSymbol deepNested in GetNestedTypes(nested))
                yield return deepNested;
        }
    }

    public Models.TypeInfo? GetType(string typeName)
    {
        if (_typeByFqn.TryGetValue(typeName, out IndexedType? byFqn))
            return MapToTypeInfo(byFqn);

        IndexedType? bySimple = _typeBySimpleName[typeName].FirstOrDefault();
        return bySimple is null ? null : MapToTypeInfo(bySimple);
    }

    public IReadOnlyList<TypeSummary> FindTypes(
        string? nameContains = null,
        string? @namespace = null,
        string? implementsInterface = null,
        string? hasAttribute = null,
        string? kind = null)
    {
        IEnumerable<IndexedType> query = _allTypes;

        if (nameContains is not null)
            query = query.Where(t => t.Symbol.Name.Contains(nameContains, StringComparison.OrdinalIgnoreCase));

        if (@namespace is not null)
        {
            query = query.Where(t =>
            {
                string ns = t.Symbol.ContainingNamespace?.ToDisplayString() ?? string.Empty;
                return ns.Equals(@namespace, StringComparison.OrdinalIgnoreCase)
                    || ns.StartsWith(@namespace + ".", StringComparison.OrdinalIgnoreCase);
            });
        }

        if (implementsInterface is not null)
        {
            query = query.Where(t => t.Symbol.AllInterfaces.Any(i =>
                i.Name.Equals(implementsInterface, StringComparison.OrdinalIgnoreCase)));
        }

        if (hasAttribute is not null)
        {
            query = query.Where(t => t.Symbol.GetAttributes().Any(a =>
                (a.AttributeClass?.Name ?? string.Empty).Contains(hasAttribute, StringComparison.OrdinalIgnoreCase)));
        }

        if (kind is not null)
        {
            query = query.Where(t => GetKind(t.Symbol).Equals(kind, StringComparison.OrdinalIgnoreCase));
        }

        return [.. query.Select(t => new TypeSummary(
            t.Symbol.Name,
            t.Symbol.ContainingNamespace?.ToDisplayString() ?? string.Empty,
            t.FilePath,
            t.LineStart,
            GetKind(t.Symbol)))];
    }

    public Models.MethodInfo? GetMethod(string typeName, string methodName)
    {
        IndexedType? indexed = FindIndexedType(typeName);
        if (indexed is null)
            return null;

        IMethodSymbol? method = indexed.Symbol.GetMembers(methodName)
            .OfType<IMethodSymbol>()
            .FirstOrDefault();

        if (method is null)
            return null;

        Location? location = method.Locations.FirstOrDefault(l => l.IsInSource);
        if (location?.SourceTree is null)
            return null;

        FileLinePositionSpan span = location.GetLineSpan();
        int lineStart = span.StartLinePosition.Line + 1;
        int lineEnd = span.EndLinePosition.Line + 1;

        SyntaxNode? root = location.SourceTree.GetRoot();
        MethodDeclarationSyntax? syntax = root
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(m =>
            {
                FileLinePositionSpan mSpan = m.GetLocation().GetLineSpan();
                return mSpan.StartLinePosition.Line + 1 == lineStart;
            });

        string body = string.Empty;
        if (syntax is not null)
        {
            body = syntax.Body?.ToString()
                ?? syntax.ExpressionBody?.ToString()
                ?? string.Empty;
        }

        string signature = method.ToDisplayString();

        string filePath = location.SourceTree.FilePath;
        if (filePath.EndsWith(".razor.g.cs", StringComparison.OrdinalIgnoreCase))
            filePath = filePath[..^".g.cs".Length];

        return new Models.MethodInfo(
            indexed.Symbol.Name,
            methodName,
            filePath,
            lineStart,
            lineEnd,
            signature,
            body);
    }

    public IReadOnlyList<ImplementationSummary> FindImplementations(string interfaceName)
    {
        return [.. _allTypes
            .Where(t =>
                t.Symbol.TypeKind != TypeKind.Interface
                && t.Symbol.AllInterfaces.Any(i =>
                    i.Name.Equals(interfaceName, StringComparison.OrdinalIgnoreCase)))
            .Select(t => new ImplementationSummary(
                t.Symbol.Name,
                t.Symbol.ContainingNamespace?.ToDisplayString() ?? string.Empty,
                t.FilePath,
                t.LineStart))];
    }

    public async Task<IReadOnlyList<UsageResult>> FindUsagesAsync(
        string symbolName,
        CancellationToken cancellationToken = default)
    {
        if (_solution is null)
            return [];

        IndexedType? indexed = FindIndexedType(symbolName);
        if (indexed is null)
            return [];

        IEnumerable<ReferencedSymbol> references = await SymbolFinder.FindReferencesAsync(
            indexed.Symbol,
            _solution,
            cancellationToken);

        List<UsageResult> results = [];

        foreach (ReferencedSymbol referencedSymbol in references)
        {
            foreach (ReferenceLocation refLocation in referencedSymbol.Locations)
            {
                Location location = refLocation.Location;
                if (!location.IsInSource || location.SourceTree is null)
                    continue;

                FileLinePositionSpan span = location.GetLineSpan();
                int lineNumber = span.StartLinePosition.Line + 1;

                SyntaxNode? root = await location.SourceTree.GetRootAsync(cancellationToken);
                SyntaxNode? node = root.FindNode(location.SourceSpan);

                string usageKind = DetermineUsageKind(node);

                string? lineText = null;
                string text = location.SourceTree.ToString();
                string[] lines = text.Split('\n');
                int lineIdx = span.StartLinePosition.Line;
                if (lineIdx >= 0 && lineIdx < lines.Length)
                    lineText = lines[lineIdx].Trim();

                string filePath = location.SourceTree.FilePath;
                if (filePath.EndsWith(".razor.g.cs", StringComparison.OrdinalIgnoreCase))
                    filePath = filePath[..^".g.cs".Length];

                results.Add(new UsageResult(filePath, lineNumber, lineText ?? string.Empty, usageKind));
            }
        }

        return results;
    }

    private static string DetermineUsageKind(SyntaxNode? node)
    {
        if (node is null)
            return "reference";

        SyntaxNode? current = node;
        while (current is not null)
        {
            if (current is BaseListSyntax)
                return "inheritance";

            if (current is ParameterSyntax parameter)
            {
                SyntaxNode? parent = parameter.Parent?.Parent;
                if (parent is ConstructorDeclarationSyntax)
                    return "injection";
                return "reference";
            }

            if (current is ObjectCreationExpressionSyntax
                or ImplicitObjectCreationExpressionSyntax)
                return "instantiation";

            if (current is InvocationExpressionSyntax)
                return "call";

            current = current.Parent;
        }

        return "reference";
    }

    public DependencyInfo? GetDependencies(string typeName)
    {
        IndexedType? indexed = FindIndexedType(typeName);
        if (indexed is null)
            return null;

        IMethodSymbol? ctor = indexed.Symbol.Constructors
            .Where(c => !c.IsStatic)
            .OrderByDescending(c => c.Parameters.Length)
            .FirstOrDefault();

        IReadOnlyList<ParameterDetail> parameters = ctor is null
            ? []
            : ctor.Parameters.Select(p => new ParameterDetail(p.Name, p.Type.ToDisplayString())).ToList();

        return new DependencyInfo(indexed.Symbol.Name, parameters, parameters);
    }

    public PublicSurface GetPublicSurface(string @namespace)
    {
        IEnumerable<IndexedType> matching = _allTypes.Where(t =>
        {
            string ns = t.Symbol.ContainingNamespace?.ToDisplayString() ?? string.Empty;
            return ns.Equals(@namespace, StringComparison.OrdinalIgnoreCase)
                || ns.StartsWith(@namespace + ".", StringComparison.OrdinalIgnoreCase);
        }).Where(t => t.Symbol.DeclaredAccessibility == Accessibility.Public);

        List<PublicSurfaceItem> interfaces = [];
        List<PublicSurfaceItem> publicClasses = [];
        List<PublicSurfaceItem> publicRecords = [];
        List<PublicSurfaceItem> enums = [];

        foreach (IndexedType t in matching)
        {
            PublicSurfaceItem item = new(t.Symbol.Name, t.FilePath);

            if (t.Symbol.TypeKind == TypeKind.Interface)
                interfaces.Add(item);
            else if (t.Symbol.TypeKind == TypeKind.Enum)
                enums.Add(item);
            else if (t.Symbol.IsRecord)
                publicRecords.Add(item);
            else if (t.Symbol.TypeKind == TypeKind.Class)
                publicClasses.Add(item);
        }

        return new PublicSurface(@namespace, interfaces, publicClasses, publicRecords, enums);
    }

    public ProjectDependency GetProjectDependencies()
    {
        if (_solution is null)
            return new ProjectDependency([], []);

        List<Models.ProjectInfo> projects = [.. _solution.Projects.Select(p => new Models.ProjectInfo(p.Name, p.FilePath ?? string.Empty))];

        List<DependencyEdge> edges = [];
        foreach (Project project in _solution.Projects)
        {
            foreach (ProjectReference reference in project.ProjectReferences)
            {
                Project? referenced = _solution.GetProject(reference.ProjectId);
                if (referenced is not null)
                    edges.Add(new DependencyEdge(project.Name, referenced.Name));
            }
        }

        return new ProjectDependency(projects, edges);
    }

    public IReadOnlyList<SymbolSearchResult> SearchSymbol(string query)
    {
        List<SymbolSearchResult> results = [];

        foreach (IndexedType indexed in _allTypes)
        {
            string fqn = indexed.Symbol.ToDisplayString();
            if (fqn.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                results.Add(new SymbolSearchResult(
                    indexed.Symbol.Name,
                    GetKind(indexed.Symbol),
                    indexed.Symbol.Name,
                    indexed.Symbol.ContainingNamespace?.ToDisplayString() ?? string.Empty,
                    indexed.FilePath,
                    indexed.LineStart));
            }

            foreach (ISymbol member in indexed.Symbol.GetMembers())
            {
                if (member is IMethodSymbol or IPropertySymbol)
                {
                    string memberFqn = member.ToDisplayString();
                    if (memberFqn.Contains(query, StringComparison.OrdinalIgnoreCase))
                    {
                        Location? loc = member.Locations.FirstOrDefault(l => l.IsInSource);
                        int lineNumber = loc is not null
                            ? loc.GetLineSpan().StartLinePosition.Line + 1
                            : indexed.LineStart;

                        string filePath = loc?.SourceTree?.FilePath ?? indexed.FilePath;
                        if (filePath.EndsWith(".razor.g.cs", StringComparison.OrdinalIgnoreCase))
                            filePath = filePath[..^".g.cs".Length];

                        string memberKind = member is IMethodSymbol ? "method" : "property";

                        results.Add(new SymbolSearchResult(
                            member.Name,
                            memberKind,
                            indexed.Symbol.Name,
                            indexed.Symbol.ContainingNamespace?.ToDisplayString() ?? string.Empty,
                            filePath,
                            lineNumber));
                    }
                }
            }
        }

        return results;
    }

    internal IEnumerable<Document> GetProjectDocuments(string projectName)
    {
        if (_solution is null)
            return [];

        return _solution.Projects
            .Where(p => p.Name.StartsWith(projectName, StringComparison.OrdinalIgnoreCase))
            .SelectMany(p => p.Documents);
    }

    internal IEnumerable<Document> GetRazorDocuments()
    {
        if (_solution is null)
            return [];

        return _solution.Projects
            .SelectMany(p => p.Documents)
            .Where(d => d.FilePath?.EndsWith(".razor", StringComparison.OrdinalIgnoreCase) == true);
    }

    internal int CountRazorDocuments()
    {
        if (_solution is null)
            return 0;

        return _solution.Projects
            .SelectMany(p => p.Documents)
            .Count(d => d.FilePath?.EndsWith(".razor", StringComparison.OrdinalIgnoreCase) == true);
    }

    internal int TotalTypeCount => _allTypes.Count;

    internal IEnumerable<(string Name, string Namespace, string FilePath, int LineStart, bool IsSealed, bool IsInterface)>
        QueryTypesSealedStatus(Func<INamedTypeSymbol, bool> predicate)
    {
        foreach (IndexedType indexed in _allTypes)
        {
            if (!predicate(indexed.Symbol))
                continue;

            yield return (
                indexed.Symbol.Name,
                indexed.Symbol.ContainingNamespace?.ToDisplayString() ?? string.Empty,
                indexed.FilePath,
                indexed.LineStart,
                indexed.Symbol.IsSealed,
                indexed.Symbol.TypeKind == TypeKind.Interface);
        }
    }

    private IndexedType? FindIndexedType(string typeName)
    {
        if (_typeByFqn.TryGetValue(typeName, out IndexedType? byFqn))
            return byFqn;

        return _typeBySimpleName[typeName].FirstOrDefault();
    }

    private static Models.TypeInfo MapToTypeInfo(IndexedType indexed)
    {
        INamedTypeSymbol symbol = indexed.Symbol;

        string? baseType = symbol.BaseType is { SpecialType: not SpecialType.System_Object }
            ? symbol.BaseType.ToDisplayString()
            : null;

        IReadOnlyList<string> interfaces = [.. symbol.Interfaces.Select(i => i.ToDisplayString())];

        IReadOnlyList<string> attributes = [.. symbol.GetAttributes()
            .Select(a => a.AttributeClass?.Name ?? string.Empty)
            .Where(n => !string.IsNullOrEmpty(n))];

        IReadOnlyList<PropertyDetail> properties = [.. symbol.GetMembers()
            .OfType<IPropertySymbol>()
            .Select(p => new PropertyDetail(p.Name, p.Type.ToDisplayString(), GetAccessibility(p.DeclaredAccessibility)))];

        IReadOnlyList<MethodSummary> methods = [.. symbol.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(m => m.MethodKind == MethodKind.Ordinary)
            .Select(m =>
            {
                Location? loc = m.Locations.FirstOrDefault(l => l.IsInSource);
                int lineStart = loc is not null ? loc.GetLineSpan().StartLinePosition.Line + 1 : 0;
                IReadOnlyList<ParameterDetail> parameters = [.. m.Parameters.Select(p => new ParameterDetail(p.Name, p.Type.ToDisplayString()))];
                return new MethodSummary(m.Name, m.ReturnType.ToDisplayString(), parameters, GetAccessibility(m.DeclaredAccessibility), lineStart);
            })];

        IMethodSymbol? ctor = symbol.Constructors
            .Where(c => !c.IsStatic)
            .OrderByDescending(c => c.Parameters.Length)
            .FirstOrDefault();

        IReadOnlyList<ParameterDetail> ctorParams = ctor is null
            ? []
            : ctor.Parameters.Select(p => new ParameterDetail(p.Name, p.Type.ToDisplayString())).ToList();

        return new Models.TypeInfo(
            symbol.Name,
            symbol.ContainingNamespace?.ToDisplayString() ?? string.Empty,
            GetKind(symbol),
            indexed.FilePath,
            indexed.LineStart,
            baseType,
            interfaces,
            attributes,
            properties,
            methods,
            ctorParams);
    }

    private static string GetKind(INamedTypeSymbol symbol)
    {
        if (symbol.IsRecord)
            return "record";

        return symbol.TypeKind switch
        {
            TypeKind.Interface => "interface",
            TypeKind.Struct => "struct",
            TypeKind.Enum => "enum",
            _ => "class"
        };
    }

    private static string GetAccessibility(Accessibility accessibility) => accessibility switch
    {
        Accessibility.Public => "public",
        Accessibility.Private => "private",
        Accessibility.Protected => "protected",
        Accessibility.Internal => "internal",
        Accessibility.ProtectedOrInternal => "protected internal",
        Accessibility.ProtectedAndInternal => "private protected",
        _ => "public"
    };

    public async Task<IReadOnlyList<DiagnosticResult>> GetCompilerDiagnosticsAsync(
        string? projectFilter = null,
        string? minSeverity = null,
        string? category = null,
        CancellationToken ct = default)
    {
        if (_solution is null)
            return [];

        DiagnosticSeverity threshold = minSeverity?.ToLowerInvariant() switch
        {
            "error" => DiagnosticSeverity.Error,
            "info" => DiagnosticSeverity.Info,
            _ => DiagnosticSeverity.Warning
        };

        List<DiagnosticResult> results = [];

        IEnumerable<Project> projects = _solution.Projects;
        if (!string.IsNullOrEmpty(projectFilter))
            projects = projects.Where(p => string.Equals(p.Name, projectFilter, StringComparison.OrdinalIgnoreCase));

        foreach (Project project in projects)
        {
            Compilation? compilation = await project.GetCompilationAsync(ct);
            if (compilation is null)
                continue;

            foreach (Diagnostic diagnostic in compilation.GetDiagnostics(ct))
            {
                if (diagnostic.Severity < threshold)
                    continue;

                string id = diagnostic.Id;
                if (!string.IsNullOrEmpty(category)
                    && !id.StartsWith(category, StringComparison.OrdinalIgnoreCase))
                    continue;

                string severityLabel = diagnostic.Severity switch
                {
                    DiagnosticSeverity.Error => "error",
                    DiagnosticSeverity.Warning => "warning",
                    DiagnosticSeverity.Info => "info",
                    _ => "hidden"
                };

                string derivedCategory = id.Length >= 2
                    ? new string(id.TakeWhile(char.IsLetter).ToArray())
                    : id;

                FileLinePositionSpan span = diagnostic.Location.GetLineSpan();
                string filePath = span.Path ?? string.Empty;
                int lineNumber = span.IsValid ? span.StartLinePosition.Line + 1 : 0;

                results.Add(new DiagnosticResult(
                    id,
                    severityLabel,
                    diagnostic.GetMessage(),
                    filePath,
                    lineNumber,
                    project.Name,
                    derivedCategory));
            }
        }

        return results;
    }

    public IReadOnlyList<TypeSummary> GetTypesInFile(string filePath)
    {
        string normalized = filePath.Replace('\\', '/');
        return [.. _allTypes
            .Where(t => t.FilePath.Replace('\\', '/').Equals(normalized, StringComparison.OrdinalIgnoreCase))
            .Select(t => new TypeSummary(
                t.Symbol.Name,
                t.Symbol.ContainingNamespace?.ToDisplayString() ?? string.Empty,
                t.FilePath,
                t.LineStart,
                GetKind(t.Symbol)))];
    }

    public void Dispose() => _workspace?.Dispose();
}
