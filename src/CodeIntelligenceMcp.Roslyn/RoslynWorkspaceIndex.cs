using CodeIntelligenceMcp.Roslyn.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
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
    private readonly IReadOnlyList<string> _loadWarnings;

    internal sealed record IndexedType(
        INamedTypeSymbol Symbol,
        Compilation Compilation,
        string ProjectName,
        string FilePath,
        int LineStart);

    private readonly Lazy<HashSet<string>> _testClassNames;
    private readonly Lazy<Task<IReadOnlyList<ProjectMethodComplexity>>> _allComplexity;

    private readonly string? _rootDir;

    private RoslynWorkspaceIndex(
        MSBuildWorkspace? workspace,
        Solution? solution,
        CleanArchitectureNames cleanArch,
        IReadOnlyList<IndexedType> allTypes,
        IReadOnlyDictionary<string, IndexedType> typeByFqn,
        ILookup<string, IndexedType> typeBySimpleName,
        IReadOnlyList<string> loadWarnings,
        string? rootDir)
    {
        _rootDir = rootDir;
        _workspace = workspace;
        _solution = solution;
        _cleanArch = cleanArch;
        _allTypes = allTypes;
        _typeByFqn = typeByFqn;
        _typeBySimpleName = typeBySimpleName;
        _loadWarnings = loadWarnings;

        _testClassNames = new Lazy<HashSet<string>>(() => new HashSet<string>(
            _allTypes
                .Where(t => t.ProjectName.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase))
                .Select(t => t.Symbol.Name),
            StringComparer.Ordinal));

        // Computed once per index instance; refresh_workspace rebuilds the whole index.
        _allComplexity = new Lazy<Task<IReadOnlyList<ProjectMethodComplexity>>>(
            () => Task.Run(() => ComplexityAnalyzer.ComputeAllAsync(this, CancellationToken.None)));
    }

    public int TypeCount => _allTypes.Count;
    public CleanArchitectureNames CleanArchitecture => _cleanArch;
    public IReadOnlyList<string> LoadWarnings => _loadWarnings;

    // Git-based staleness signal captured at build time; null when the workspace is not a git repo.
    public string? Fingerprint { get; internal set; }
    public DateTime IndexedAtUtc { get; } = DateTime.UtcNow;

    public bool IsStale()
    {
        if (Fingerprint is null || _solution?.FilePath is null)
            return false;

        string? current = Git.GitDiffService.ComputeFingerprint(_solution.FilePath);
        return current is not null && current != Fingerprint;
    }

    private readonly object _staleLock = new();
    private DateTime _staleCheckedAtUtc;
    private bool _lastStale;

    // TTL-cached staleness for per-response flags: the fingerprint runs a full git status,
    // too expensive to pay on every tool call. ChangeAnalysisTool keeps using IsStale()
    // directly because its auto-refresh needs the precise answer.
    public bool IsStaleCached()
    {
        lock (_staleLock)
        {
            if (DateTime.UtcNow - _staleCheckedAtUtc > TimeSpan.FromSeconds(10))
            {
                _lastStale = IsStale();
                _staleCheckedAtUtc = DateTime.UtcNow;
            }
            return _lastStale;
        }
    }

    internal IReadOnlyList<IndexedType> AllTypes => _allTypes;
    internal Solution? Solution => _solution;

    // Response paths are workspace-relative with forward slashes: shorter for the client and
    // stable across machines. Internal storage stays absolute — analyzers read from disk.
    internal string Rel(string path)
    {
        if (string.IsNullOrEmpty(_rootDir) || string.IsNullOrEmpty(path) || !Path.IsPathRooted(path))
            return path;

        string relative = Path.GetRelativePath(_rootDir, path);
        return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative)
            ? path
            : relative.Replace('\\', '/');
    }
    internal HashSet<string> TestClassNames => _testClassNames.Value;

    internal Task<IReadOnlyList<ProjectMethodComplexity>> GetAllComplexityAsync(CancellationToken ct = default)
        => _allComplexity.Value.WaitAsync(ct);

    // Creates an index from in-memory compilations for unit testing.
    // GetProjectDocuments, GetRazorDocuments, and FindUsagesAsync are not available in this mode.
    internal static RoslynWorkspaceIndex CreateForTesting(
        IEnumerable<(Compilation Compilation, string ProjectName)> compilations,
        CleanArchitectureNames cleanArch,
        string? rootDir = null)
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

        return new RoslynWorkspaceIndex(null, null, cleanArch, allTypes, typeByFqn, typeBySimpleName, [], rootDir);
    }

    public static async Task<RoslynWorkspaceIndex> BuildAsync(
        MSBuildWorkspace workspace,
        Solution solution,
        CleanArchitectureNames cleanArch,
        IReadOnlyList<string>? loadWarnings = null,
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

        string? rootDir = solution.FilePath is not null ? Path.GetDirectoryName(solution.FilePath) : null;
        return new RoslynWorkspaceIndex(workspace, solution, effectiveCleanArch, allTypes, typeByFqn, typeBySimpleName, loadWarnings ?? [], rootDir);
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
            query = query.Where(t => SymbolQueryMatcher.Matches(nameContains, t.Symbol.Name));

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
                InterfaceMatches(i, implementsInterface)));
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
            Rel(t.FilePath),
            t.LineStart,
            GetKind(t.Symbol)))];
    }

    public Models.MethodInfo? GetMethod(string typeName, string methodName)
    {
        IndexedType? indexed = FindIndexedType(typeName);
        if (indexed is null)
            return null;

        IReadOnlyList<IMethodSymbol> overloads = [.. indexed.Symbol.GetMembers(methodName)
            .OfType<IMethodSymbol>()
            .Where(m => m.MethodKind == MethodKind.Ordinary)];

        IMethodSymbol? method = overloads.FirstOrDefault();
        if (method is null)
            return null;

        Location? location = method.Locations.FirstOrDefault(l => l.IsInSource);
        if (location?.SourceTree is null)
            return null;

        FileLinePositionSpan span = location.GetLineSpan();
        int lineStart = span.StartLinePosition.Line + 1;
        int lineEnd = span.EndLinePosition.Line + 1;

        MethodDeclarationSyntax? syntax = method.DeclaringSyntaxReferences
            .Select(r => r.GetSyntax())
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault();

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
            Rel(filePath),
            lineStart,
            lineEnd,
            signature,
            body,
            overloads.Count);
    }

    public IReadOnlyList<ImplementationSummary> FindImplementations(string interfaceName)
    {
        return [.. _allTypes
            .Where(t =>
                t.Symbol.TypeKind != TypeKind.Interface
                && t.Symbol.AllInterfaces.Any(i => InterfaceMatches(i, interfaceName)))
            .Select(t => new ImplementationSummary(
                t.Symbol.Name,
                t.Symbol.ContainingNamespace?.ToDisplayString() ?? string.Empty,
                Rel(t.FilePath),
                t.LineStart))];
    }

    // Walks BaseType chains, so descendants at any depth are found. Matches on simple name
    // or fully qualified name of any ancestor class.
    public IReadOnlyList<ImplementationSummary> FindDerivedTypes(string baseTypeName)
    {
        List<ImplementationSummary> results = [];

        foreach (IndexedType t in _allTypes)
        {
            for (INamedTypeSymbol? b = t.Symbol.BaseType; b is not null; b = b.BaseType)
            {
                if (b.Name.Equals(baseTypeName, StringComparison.OrdinalIgnoreCase)
                    || b.ToDisplayString().Equals(baseTypeName, StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(new ImplementationSummary(
                        t.Symbol.Name,
                        t.Symbol.ContainingNamespace?.ToDisplayString() ?? string.Empty,
                        Rel(t.FilePath),
                        t.LineStart));
                    break;
                }
            }
        }

        return results;
    }

    // "IUseCase" matches every construction; "IUseCase<ReqA, int>" matches only interfaces
    // with that generic name, arity, and type-argument simple names.
    private static bool InterfaceMatches(INamedTypeSymbol candidate, string query)
    {
        int lt = query.IndexOf('<');
        if (lt < 0)
            return candidate.Name.Equals(query, StringComparison.OrdinalIgnoreCase);

        if (!query.EndsWith('>'))
            return false;

        string name = query[..lt].Trim();
        string[] args = query[(lt + 1)..^1].Split(',');

        if (!candidate.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
            || candidate.TypeArguments.Length != args.Length)
            return false;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i].Trim();
            ITypeSymbol typeArg = candidate.TypeArguments[i];

            // ToDisplayString covers keyword forms ("int" for Int32) and qualified names.
            if (!typeArg.Name.Equals(arg, StringComparison.OrdinalIgnoreCase)
                && !typeArg.ToDisplayString().Equals(arg, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
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
            PublicSurfaceItem item = new(t.Symbol.Name, Rel(t.FilePath));

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

        // Wildcard queries match on the bare symbol name; substring queries keep the historical
        // FQN match so namespace fragments keep working.
        bool wildcards = SymbolQueryMatcher.HasWildcards(query);

        foreach (IndexedType indexed in _allTypes)
        {
            string fqn = indexed.Symbol.ToDisplayString();
            bool typeMatch = wildcards
                ? SymbolQueryMatcher.Matches(query, indexed.Symbol.Name)
                : fqn.Contains(query, StringComparison.OrdinalIgnoreCase);

            if (typeMatch)
            {
                results.Add(new SymbolSearchResult(
                    indexed.Symbol.Name,
                    GetKind(indexed.Symbol),
                    indexed.Symbol.Name,
                    indexed.Symbol.ContainingNamespace?.ToDisplayString() ?? string.Empty,
                    Rel(indexed.FilePath),
                    indexed.LineStart));
            }

            foreach (ISymbol member in indexed.Symbol.GetMembers())
            {
                if (member is IMethodSymbol or IPropertySymbol)
                {
                    // Compiler-generated members (record property accessors, EqualityContract,
                    // synthesized constructors) are pure noise in search results.
                    if (member.IsImplicitlyDeclared)
                        continue;
                    if (member is IMethodSymbol method
                        && method.MethodKind is not (MethodKind.Ordinary or MethodKind.Constructor or MethodKind.LocalFunction))
                        continue;
                    if (member is IPropertySymbol { Name: "EqualityContract" })
                        continue;

                    bool memberMatch = wildcards
                        ? SymbolQueryMatcher.Matches(query, member.Name)
                        : member.ToDisplayString().Contains(query, StringComparison.OrdinalIgnoreCase);

                    if (memberMatch)
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
                            Rel(filePath),
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
                covered.Add(new CoveredUseCase(uc.Symbol.Name, test.Symbol.Name, Rel(test.FilePath)));
            else
                uncovered.Add(new UncoveredUseCase(
                    uc.Symbol.Name,
                    uc.Symbol.ContainingNamespace?.ToDisplayString() ?? string.Empty,
                    Rel(uc.FilePath)));
        }

        double pct = useCases.Count == 0 ? 0 : Math.Round(100.0 * covered.Count / useCases.Count, 1);
        return new TestCoverageResult(useCases.Count, covered.Count, pct, uncovered, covered);
    }

    internal IReadOnlyList<IndexedType> FindIndexedTypes(string typeName)
    {
        if (_typeByFqn.TryGetValue(typeName, out IndexedType? byFqn))
            return [byFqn];

        return [.. _typeBySimpleName[typeName]];
    }

    // Fully qualified names of every type matching the given simple or qualified name.
    // Callers use this to report ambiguity instead of silently picking the first match.
    public IReadOnlyList<string> GetTypeCandidates(string typeName)
    {
        return [.. FindIndexedTypes(typeName).Select(t => t.Symbol.ToDisplayString())];
    }

    internal IndexedType? FindIndexedType(string typeName) => FindIndexedTypes(typeName).FirstOrDefault();

    private Models.TypeInfo MapToTypeInfo(IndexedType indexed)
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
            Rel(indexed.FilePath),
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
                    Rel(filePath),
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
                Rel(t.FilePath),
                t.LineStart,
                GetKind(t.Symbol)))];
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
