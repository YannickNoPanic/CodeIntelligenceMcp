using System.Collections.Immutable;
using CodeIntelligenceMcp.Roslyn.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Text;

namespace CodeIntelligenceMcp.Roslyn;

public sealed class ReferenceQueries(RoslynWorkspaceIndex index)
{
    public TargetLookup LookupUsageTarget(string symbolName) =>
        SymbolTargetResolver.ResolveUsageTarget(index, symbolName).Lookup;

    public TargetLookup LookupCallerTarget(string typeName, string methodName) =>
        SymbolTargetResolver.ResolveCallerTarget(index, typeName, methodName).Lookup;

    public async Task<IReadOnlyList<UsageResult>> FindUsagesAsync(
        string symbolName,
        CancellationToken ct = default)
    {
        if (index.Solution is null)
            return [];

        (IReadOnlyList<ISymbol> targets, _) = SymbolTargetResolver.ResolveUsageTarget(index, symbolName);

        List<ReferencedSymbol> references = [];
        foreach (ISymbol target in targets)
            references.AddRange(await SymbolFinder.FindReferencesAsync(target, index.Solution, index.ScopedDocuments, ct));

        List<UsageResult> results = [];
        HashSet<(string File, int Start)> seen = [];

        foreach (ReferencedSymbol referencedSymbol in references)
        {
            foreach (ReferenceLocation refLocation in referencedSymbol.Locations)
            {
                Location location = refLocation.Location;
                if (!location.IsInSource || location.SourceTree is null
                    || !seen.Add((location.SourceTree.FilePath, location.SourceSpan.Start)))
                    continue;

                FileLinePositionSpan span = location.GetLineSpan();
                int lineNumber = span.StartLinePosition.Line + 1;

                SyntaxNode root = await location.SourceTree.GetRootAsync(ct);
                SyntaxNode? node = root.FindNode(location.SourceSpan);

                string usageKind = DetermineUsageKind(node);
                string lineText = await GetLineTextAsync(location.SourceTree, span.StartLinePosition.Line, ct);

                results.Add(new UsageResult(
                    index.Rel(NormalizeRazorPath(location.SourceTree.FilePath)),
                    lineNumber,
                    lineText,
                    usageKind));
            }
        }

        return results;
    }

    public async Task<IReadOnlyList<CallerResult>> FindCallersAsync(
        string typeName,
        string methodName,
        CancellationToken ct = default)
        => await FindCallersAsync(typeName, methodName, 1, ct);

    // BFS over caller levels: depth 1 = direct callers of every ordinary overload; each
    // further level runs SymbolFinder on the enclosing methods found one level down.
    // The frontier is capped per level to bound cost on hub methods.
    public async Task<IReadOnlyList<CallerResult>> FindCallersAsync(
        string typeName,
        string methodName,
        int depth,
        CancellationToken ct = default)
    {
        const int maxFrontierPerLevel = 200;

        if (index.Solution is null)
            return [];

        (IReadOnlyList<IMethodSymbol> targets, _) = SymbolTargetResolver.ResolveCallerTarget(index, typeName, methodName);
        List<IMethodSymbol> frontier = [.. targets];
        if (frontier.Count == 0)
            return [];

        List<CallerResult> results = [];
        HashSet<(string Type, string Method, string File, int Line)> seen = [];

        for (int level = 1; level <= depth && frontier.Count > 0; level++)
        {
            List<IMethodSymbol> nextFrontier = [];

            foreach (IMethodSymbol method in frontier)
            {
                IEnumerable<ReferencedSymbol> refs = await SymbolFinder.FindReferencesAsync(method, index.Solution, index.ScopedDocuments, ct);

                foreach (ReferencedSymbol referencedSymbol in refs)
                {
                    foreach (ReferenceLocation refLocation in referencedSymbol.Locations)
                    {
                        Location location = refLocation.Location;
                        if (!location.IsInSource || location.SourceTree is null)
                            continue;

                        SyntaxNode root = await location.SourceTree.GetRootAsync(ct);
                        SyntaxNode? node = root.FindNode(location.SourceSpan);

                        (string callerType, string callerMethod, SyntaxNode? enclosingDecl) = FindEnclosingMember(node);

                        FileLinePositionSpan span = location.GetLineSpan();
                        int lineNumber = span.StartLinePosition.Line + 1;

                        if (!seen.Add((callerType, callerMethod, location.SourceTree.FilePath, lineNumber)))
                            continue;

                        string lineText = await GetLineTextAsync(location.SourceTree, span.StartLinePosition.Line, ct);

                        results.Add(new CallerResult(
                            callerType,
                            callerMethod,
                            index.Rel(NormalizeRazorPath(location.SourceTree.FilePath)),
                            lineNumber,
                            lineText,
                            level));

                        if (level < depth
                            && nextFrontier.Count < maxFrontierPerLevel
                            && enclosingDecl is not null
                            && index.Solution.GetDocument(location.SourceTree) is { } doc
                            && await doc.GetSemanticModelAsync(ct) is { } semanticModel
                            && semanticModel.GetDeclaredSymbol(enclosingDecl, ct) is IMethodSymbol callerSymbol)
                        {
                            nextFrontier.Add(callerSymbol);
                        }
                    }
                }
            }

            frontier = nextFrontier;
        }

        return results;
    }

    private static (string Type, string Method, SyntaxNode? Declaration) FindEnclosingMember(SyntaxNode? node)
    {
        string callerType = "<unknown>";
        string callerMethod = "<unknown>";
        SyntaxNode? declaration = null;

        SyntaxNode? current = node;
        while (current is not null)
        {
            if (current is MethodDeclarationSyntax md && callerMethod == "<unknown>")
            {
                callerMethod = md.Identifier.Text;
                declaration = md;
            }
            else if (current is ConstructorDeclarationSyntax cd && callerMethod == "<unknown>")
            {
                callerMethod = ".ctor";
                declaration = cd;
            }

            if (current is TypeDeclarationSyntax td)
            {
                callerType = td.Identifier.Text;
                break;
            }
            current = current.Parent;
        }

        return (callerType, callerMethod, declaration);
    }

    public async Task<IReadOnlyList<DeadCodeResult>> FindDeadCodeAsync(
        string? projectFilter = null,
        CancellationToken ct = default)
    {
        Solution? solution = index.Solution;
        if (solution is null)
            return [];

        List<DeadCodeResult> results = [];

        foreach (RoslynWorkspaceIndex.IndexedType indexed in index.AllTypes)
        {
            INamedTypeSymbol symbol = indexed.Symbol;

            if (index.IsTestProject(indexed.ProjectName))
                continue;

            if (projectFilter is not null
                && !indexed.ProjectName.Contains(projectFilter, StringComparison.OrdinalIgnoreCase))
                continue;

            // Private members can only be referenced from documents declaring the (possibly
            // partial) type — scoping the search there avoids a whole-solution scan per member.
            IImmutableSet<Document>? scope = GetDeclaringDocuments(solution, symbol);

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

                IEnumerable<ReferencedSymbol> refs = scope is null
                    ? await SymbolFinder.FindReferencesAsync(member, solution, ct)
                    : await SymbolFinder.FindReferencesAsync(member, solution, scope, ct);

                if (refs.Any(r => r.Locations.Any()))
                    continue;

                Location? loc = member.Locations.FirstOrDefault(l => l.IsInSource);
                int line = loc is not null ? loc.GetLineSpan().StartLinePosition.Line + 1 : indexed.LineStart;

                results.Add(new DeadCodeResult(symbol.Name, member.Name, memberKind, index.Rel(indexed.FilePath), line));
            }
        }

        return results;
    }

    private static IImmutableSet<Document>? GetDeclaringDocuments(Solution solution, INamedTypeSymbol symbol)
    {
        HashSet<Document> documents = [];

        foreach (SyntaxReference syntaxRef in symbol.DeclaringSyntaxReferences)
        {
            Document? document = solution.GetDocument(syntaxRef.SyntaxTree);
            if (document is not null)
                documents.Add(document);
        }

        return documents.Count > 0 ? documents.ToImmutableHashSet() : null;
    }

    private static async Task<string> GetLineTextAsync(SyntaxTree tree, int lineIndex, CancellationToken ct)
    {
        SourceText text = await tree.GetTextAsync(ct);
        if (lineIndex < 0 || lineIndex >= text.Lines.Count)
            return string.Empty;

        return text.Lines[lineIndex].ToString().Trim();
    }

    private static string NormalizeRazorPath(string filePath) =>
        filePath.EndsWith(".razor.g.cs", StringComparison.OrdinalIgnoreCase)
            ? filePath[..^".g.cs".Length]
            : filePath;

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
}
