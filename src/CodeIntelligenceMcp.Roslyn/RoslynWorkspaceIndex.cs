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

    internal IEnumerable<string> GetRazorFilePaths()
    {
        if (_solution is null)
            yield break;

        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        foreach (Project project in _solution.Projects)
        {
            string? projectDir = Path.GetDirectoryName(project.FilePath);
            if (string.IsNullOrEmpty(projectDir) || !Directory.Exists(projectDir))
                continue;

            foreach (string razorFile in Directory.GetFiles(projectDir, "*.razor", SearchOption.AllDirectories))
            {
                if (razorFile.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                    || razorFile.Contains("/obj/", StringComparison.Ordinal))
                    continue;

                if (seen.Add(razorFile))
                    yield return razorFile;
            }
        }
    }

    internal IEnumerable<Document> GetAllDocuments(bool skipTests = true, bool skipGenerated = true)
    {
        if (_solution is null)
            return [];

        IEnumerable<Project> projects = _solution.Projects;

        if (skipTests)
            projects = projects.Where(p => !p.Name.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase));

        IEnumerable<Document> docs = projects.SelectMany(p => p.Documents);

        if (skipGenerated)
            docs = docs.Where(d => d.FilePath?.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) != true);

        return docs;
    }

    public async Task<IReadOnlyList<Models.DeadCodeResult>> FindDeadCodeAsync(
        string? projectFilter = null,
        CancellationToken ct = default)
    {
        if (_solution is null)
            return [];

        List<Models.DeadCodeResult> results = [];

        foreach ((INamedTypeSymbol symbol, string projectName, string filePath, int lineStart) in QueryTypes())
        {
            if (projectName.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase))
                continue;

            if (projectFilter is not null
                && !projectName.Contains(projectFilter, StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (ISymbol member in symbol.GetMembers())
            {
                if (member.DeclaredAccessibility != Accessibility.Private)
                    continue;

                if (member.IsImplicitlyDeclared)
                    continue;

                // Skip compiler-generated names (e.g. <Main>$, backing fields)
                if (member.Name.Contains('<'))
                    continue;

                // Skip synthesized record members that never have user-level references
                if (symbol.IsRecord && member.Name is "EqualityContract" or "PrintMembers")
                    continue;

                string memberKind;
                switch (member)
                {
                    case IMethodSymbol m when m.MethodKind == MethodKind.Ordinary && !m.IsImplicitlyDeclared:
                        memberKind = "method";
                        break;
                    case IPropertySymbol p when !p.IsImplicitlyDeclared:
                        memberKind = "property";
                        break;
                    case IFieldSymbol f when !f.IsImplicitlyDeclared:
                        memberKind = "field";
                        break;
                    default:
                        continue;
                }

                IEnumerable<ReferencedSymbol> refs = await SymbolFinder.FindReferencesAsync(
                    member, _solution, ct);

                if (refs.Any(r => r.Locations.Any()))
                    continue;

                Location? loc = member.Locations.FirstOrDefault(l => l.IsInSource);
                int line = loc is not null ? loc.GetLineSpan().StartLinePosition.Line + 1 : lineStart;

                results.Add(new Models.DeadCodeResult(symbol.Name, member.Name, memberKind, filePath, line));
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

    internal IEnumerable<(INamedTypeSymbol Symbol, string ProjectName, string FilePath, int LineStart)>
        QueryTypes(Func<INamedTypeSymbol, bool>? predicate = null)
    {
        foreach (IndexedType indexed in _allTypes)
        {
            if (predicate is null || predicate(indexed.Symbol))
                yield return (indexed.Symbol, indexed.ProjectName, indexed.FilePath, indexed.LineStart);
        }
    }

    public TestCoverageResult GetTestCoverage()
    {
        List<IndexedType> useCases = [.. _allTypes.Where(t =>
            !t.ProjectName.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase)
            && t.Symbol.TypeKind == TypeKind.Class
            && !t.Symbol.IsAbstract
            && (t.Symbol.Name.EndsWith("UseCase", StringComparison.Ordinal)
                || t.Symbol.AllInterfaces.Any(i => i.Name.StartsWith("IUseCase", StringComparison.OrdinalIgnoreCase))))];

        ILookup<string, IndexedType> testsByName = _allTypes
            .Where(t => t.ProjectName.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase))
            .ToLookup(t => t.Symbol.Name, StringComparer.Ordinal);

        List<CoveredUseCase> covered = [];
        List<UncoveredUseCase> uncovered = [];

        foreach (IndexedType uc in useCases)
        {
            IndexedType? test = testsByName[uc.Symbol.Name + "Tests"].FirstOrDefault();
            if (test is not null)
                covered.Add(new CoveredUseCase(uc.Symbol.Name, test.Symbol.Name, test.FilePath));
            else
                uncovered.Add(new UncoveredUseCase(
                    uc.Symbol.Name,
                    uc.Symbol.ContainingNamespace?.ToDisplayString() ?? string.Empty,
                    uc.FilePath));
        }

        double pct = useCases.Count == 0 ? 0 : Math.Round(100.0 * covered.Count / useCases.Count, 1);
        return new TestCoverageResult(useCases.Count, covered.Count, pct, uncovered, covered);
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

    public async Task<IReadOnlyList<CallerResult>> FindCallersAsync(
        string typeName,
        string methodName,
        CancellationToken ct = default)
    {
        if (_solution is null)
            return [];

        IndexedType? indexed = FindIndexedType(typeName);
        if (indexed is null)
            return [];

        IMethodSymbol? method = indexed.Symbol.GetMembers(methodName)
            .OfType<IMethodSymbol>()
            .FirstOrDefault(m => m.MethodKind == MethodKind.Ordinary);
        if (method is null)
            return [];

        IEnumerable<ReferencedSymbol> refs = await SymbolFinder.FindReferencesAsync(method, _solution, ct);

        List<CallerResult> results = [];

        foreach (ReferencedSymbol referencedSymbol in refs)
        {
            foreach (ReferenceLocation refLocation in referencedSymbol.Locations)
            {
                Location location = refLocation.Location;
                if (!location.IsInSource || location.SourceTree is null)
                    continue;

                SyntaxNode root = await location.SourceTree.GetRootAsync(ct);
                SyntaxNode? node = root.FindNode(location.SourceSpan);

                string callerType = "<unknown>";
                string callerMethod = "<unknown>";
                SyntaxNode? current = node;
                while (current is not null)
                {
                    if (current is MethodDeclarationSyntax md && callerMethod == "<unknown>")
                        callerMethod = md.Identifier.Text;
                    else if (current is ConstructorDeclarationSyntax && callerMethod == "<unknown>")
                        callerMethod = ".ctor";

                    if (current is TypeDeclarationSyntax td)
                    {
                        callerType = td.Identifier.Text;
                        break;
                    }
                    current = current.Parent;
                }

                FileLinePositionSpan span = location.GetLineSpan();
                int lineNumber = span.StartLinePosition.Line + 1;

                string lineText = string.Empty;
                string[] lines = location.SourceTree.ToString().Split('\n');
                int lineIdx = span.StartLinePosition.Line;
                if (lineIdx >= 0 && lineIdx < lines.Length)
                    lineText = lines[lineIdx].Trim();

                string filePath = location.SourceTree.FilePath;
                if (filePath.EndsWith(".razor.g.cs", StringComparison.OrdinalIgnoreCase))
                    filePath = filePath[..^".g.cs".Length];

                results.Add(new CallerResult(callerType, callerMethod, filePath, lineNumber, lineText));
            }
        }

        return results;
    }

    public IReadOnlyList<TypeCoupling> GetCoupling(string? projectFilter = null, int minCoupling = 5)
    {
        List<TypeCoupling> results = [];

        foreach (IndexedType indexed in _allTypes)
        {
            if (indexed.ProjectName.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase))
                continue;

            if (projectFilter is not null
                && !indexed.ProjectName.Contains(projectFilter, StringComparison.OrdinalIgnoreCase))
                continue;

            HashSet<string> dependsOn = [];

            foreach (ISymbol member in indexed.Symbol.GetMembers())
            {
                IEnumerable<ITypeSymbol> types = member switch
                {
                    IFieldSymbol f => [f.Type],
                    IPropertySymbol p => [p.Type],
                    IMethodSymbol m => [.. m.Parameters.Select(p => p.Type), m.ReturnType],
                    _ => []
                };

                foreach (ITypeSymbol t in types)
                    CollectExternalTypes(t, indexed.Symbol, dependsOn);
            }

            if (dependsOn.Count < minCoupling)
                continue;

            results.Add(new TypeCoupling(
                indexed.Symbol.Name,
                indexed.FilePath,
                indexed.LineStart,
                dependsOn.Count,
                [.. dependsOn.OrderBy(x => x)]));
        }

        return [.. results.OrderByDescending(r => r.EfferentCoupling)];
    }

    private static void CollectExternalTypes(ITypeSymbol type, INamedTypeSymbol owner, HashSet<string> collected)
    {
        if (type.SpecialType != SpecialType.None)
            return;

        if (SymbolEqualityComparer.Default.Equals(type, owner))
            return;

        if (type is INamedTypeSymbol named)
        {
            string name = named.Name;
            bool isNoise = name is "Void" or "Task" or "ValueTask" or "CancellationToken"
                or "IEnumerable" or "IReadOnlyList" or "IList" or "List" or "Dictionary"
                or "IReadOnlyDictionary" or "HashSet" or "ISet" or "Exception" or "Nullable"
                or "Object" or "String" or "JsonElement" or "JsonDocument";

            if (!isNoise && !string.IsNullOrEmpty(name))
                collected.Add(name);

            foreach (ITypeSymbol arg in named.TypeArguments)
                CollectExternalTypes(arg, owner, collected);
        }
        else if (type is IArrayTypeSymbol array)
        {
            CollectExternalTypes(array.ElementType, owner, collected);
        }
    }

    public async Task<ChangeRiskResult?> GetChangeRiskAsync(string typeName, CancellationToken ct = default)
    {
        if (_solution is null)
            return null;

        IndexedType? indexed = FindIndexedType(typeName);
        if (indexed is null)
            return null;

        // 1. Referencing types — how many distinct types reference this one
        IEnumerable<ReferencedSymbol> refs = await SymbolFinder.FindReferencesAsync(indexed.Symbol, _solution, ct);

        HashSet<string> referencingTypeNames = new(StringComparer.Ordinal);
        foreach (ReferencedSymbol refSym in refs)
        {
            foreach (ReferenceLocation loc in refSym.Locations)
            {
                if (!loc.Location.IsInSource || loc.Location.SourceTree is null)
                    continue;

                SyntaxNode root = await loc.Location.SourceTree.GetRootAsync(ct);
                SyntaxNode? node = root.FindNode(loc.Location.SourceSpan);
                SyntaxNode? current = node;

                while (current is not null)
                {
                    if (current is TypeDeclarationSyntax td)
                    {
                        if (!td.Identifier.Text.Equals(indexed.Symbol.Name, StringComparison.Ordinal))
                            referencingTypeNames.Add(td.Identifier.Text);
                        break;
                    }
                    current = current.Parent;
                }
            }
        }

        // 2. Coupling
        HashSet<string> couplingSet = new(StringComparer.Ordinal);
        foreach (ISymbol member in indexed.Symbol.GetMembers())
        {
            IEnumerable<ITypeSymbol> types = member switch
            {
                IFieldSymbol f => [f.Type],
                IPropertySymbol p => [p.Type],
                IMethodSymbol m => [.. m.Parameters.Select(p => p.Type), m.ReturnType],
                _ => []
            };
            foreach (ITypeSymbol t in types)
                CollectExternalTypes(t, indexed.Symbol, couplingSet);
        }

        // 3. Complexity — scoped to this type only
        ComplexityAnalyzer complexityAnalyzer = new(this);
        IReadOnlyList<MethodComplexity> allMethods = complexityAnalyzer.Analyze(
            minComplexity: 1, typeFilter: indexed.Symbol.Name);
        int maxComplexity = allMethods.Count > 0 ? allMethods.Max(m => m.Complexity) : 1;
        IReadOnlyList<MethodComplexity> hotspots = [.. allMethods
            .Where(m => m.Complexity >= 5)
            .OrderByDescending(m => m.Complexity)];

        // 4. Tests — convention: XxxTests class in a .Tests project
        bool hasTests = _allTypes.Any(t =>
            t.ProjectName.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase)
            && t.Symbol.Name.Equals(indexed.Symbol.Name + "Tests", StringComparison.Ordinal));

        // 5. Score
        int referencingCount = referencingTypeNames.Count;
        int coupling = couplingSet.Count;

        int refScore = referencingCount switch { 0 => 0, <= 3 => 10, <= 10 => 20, _ => 30 };
        int couplingScore = coupling switch { <= 4 => 0, <= 9 => 8, <= 14 => 17, _ => 25 };
        int complexityScore = maxComplexity switch { <= 4 => 0, <= 9 => 8, <= 14 => 17, _ => 25 };
        int testScore = hasTests ? 0 : 20;
        int totalScore = refScore + couplingScore + complexityScore + testScore;

        string riskLabel = totalScore switch { <= 20 => "low", <= 50 => "medium", <= 75 => "high", _ => "very-high" };

        List<string> summaryParts = [];
        if (referencingCount > 5) summaryParts.Add($"{referencingCount} referencing types");
        if (coupling > 10) summaryParts.Add($"coupling {coupling}");
        if (maxComplexity >= 10) summaryParts.Add($"max complexity {maxComplexity}");
        if (!hasTests) summaryParts.Add("no tests");

        string detail = summaryParts.Count > 0 ? ": " + string.Join(", ", summaryParts) : string.Empty;
        string summary = $"{char.ToUpperInvariant(riskLabel[0]) + riskLabel[1..]} risk ({totalScore}/100){detail}.";

        return new ChangeRiskResult(
            indexed.Symbol.Name,
            totalScore,
            riskLabel,
            referencingCount,
            coupling,
            maxComplexity,
            hasTests,
            [.. referencingTypeNames.OrderBy(x => x)],
            hotspots,
            summary);
    }

    public IReadOnlyList<HotspotResult> GetHotspots(int topN = 20, string? projectFilter = null)
    {
        ComplexityAnalyzer complexityAnalyzer = new(this);
        IReadOnlyList<MethodComplexity> allComplexity = complexityAnalyzer.Analyze(minComplexity: 1, projectFilter: projectFilter);

        // Group max complexity per type
        Dictionary<string, int> maxByType = allComplexity
            .GroupBy(m => m.TypeName)
            .ToDictionary(g => g.Key, g => g.Max(m => m.Complexity), StringComparer.Ordinal);

        // Build coupling per type using already-indexed types
        List<HotspotResult> results = [];

        foreach (IndexedType indexed in _allTypes)
        {
            if (indexed.ProjectName.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase))
                continue;
            if (indexed.FilePath.Contains("/obj/", StringComparison.Ordinal)
                || indexed.FilePath.Contains("\\obj\\", StringComparison.Ordinal))
                continue;
            if (projectFilter is not null
                && !indexed.ProjectName.Contains(projectFilter, StringComparison.OrdinalIgnoreCase))
                continue;
            if (indexed.Symbol.TypeKind == TypeKind.Interface)
                continue;
            // DbContext has trivially high coupling — not an actionable hotspot
            if (indexed.Symbol.BaseType?.Name == "DbContext")
                continue;

            HashSet<string> couplingSet = new(StringComparer.Ordinal);
            foreach (ISymbol member in indexed.Symbol.GetMembers())
            {
                IEnumerable<ITypeSymbol> types = member switch
                {
                    IFieldSymbol f => [f.Type],
                    IPropertySymbol p => [p.Type],
                    IMethodSymbol m => [.. m.Parameters.Select(p => p.Type), m.ReturnType],
                    _ => []
                };
                foreach (ITypeSymbol t in types)
                    CollectExternalTypes(t, indexed.Symbol, couplingSet);
            }

            int coupling = couplingSet.Count;
            int maxComplexity = maxByType.GetValueOrDefault(indexed.Symbol.Name, 1);
            bool hasTests = _allTypes.Any(t =>
                t.ProjectName.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase)
                && t.Symbol.Name.Equals(indexed.Symbol.Name + "Tests", StringComparison.Ordinal));

            int couplingScore = coupling switch { <= 4 => 0, <= 9 => 8, <= 14 => 17, _ => 25 };
            int complexityScore = maxComplexity switch { <= 4 => 0, <= 9 => 8, <= 14 => 17, _ => 25 };
            int testScore = hasTests ? 0 : 20;
            int score = couplingScore + complexityScore + testScore;

            if (score == 0)
                continue;

            List<string> reasons = [];
            if (coupling > 9) reasons.Add($"coupling {coupling}");
            if (maxComplexity >= 8) reasons.Add($"complexity {maxComplexity}");
            if (!hasTests) reasons.Add("no tests");
            string reason = string.Join(", ", reasons);

            results.Add(new HotspotResult(
                indexed.Symbol.Name,
                indexed.FilePath,
                indexed.LineStart,
                score,
                coupling,
                maxComplexity,
                hasTests,
                reason));
        }

        return [.. results.OrderByDescending(r => r.HotspotScore).Take(topN)];
    }

    public IReadOnlyList<IReadOnlyList<string>> FindCircularDependencies()
    {
        if (_solution is null)
            return [];

        ProjectDependencyGraph graph = _solution.GetProjectDependencyGraph();

        Dictionary<ProjectId, string> nameById = _solution.Projects
            .ToDictionary(p => p.Id, p => p.Name);

        Dictionary<string, HashSet<string>> adjacency = [];
        foreach (Project project in _solution.Projects)
        {
            string name = project.Name;
            if (!adjacency.ContainsKey(name))
                adjacency[name] = [];

            foreach (ProjectId dep in graph.GetProjectsThatThisProjectDirectlyDependsOn(project.Id))
            {
                if (nameById.TryGetValue(dep, out string? depName))
                    adjacency[name].Add(depName);
            }
        }

        List<IReadOnlyList<string>> cycles = [];
        HashSet<string> visited = new(StringComparer.Ordinal);
        HashSet<string> stack = new(StringComparer.Ordinal);
        List<string> path = [];

        void Dfs(string node)
        {
            visited.Add(node);
            stack.Add(node);
            path.Add(node);

            foreach (string neighbor in adjacency.GetValueOrDefault(node, []))
            {
                if (stack.Contains(neighbor))
                {
                    int cycleStart = path.IndexOf(neighbor);
                    cycles.Add([.. path[cycleStart..], neighbor]);
                }
                else if (!visited.Contains(neighbor))
                {
                    Dfs(neighbor);
                }
            }

            stack.Remove(node);
            path.RemoveAt(path.Count - 1);
        }

        foreach (string node in adjacency.Keys)
        {
            if (!visited.Contains(node))
                Dfs(node);
        }

        return cycles;
    }

    public void Dispose() => _workspace?.Dispose();
}
