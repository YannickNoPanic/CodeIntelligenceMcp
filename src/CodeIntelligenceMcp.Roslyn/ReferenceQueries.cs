using System.Collections.Immutable;
using CodeIntelligenceMcp.Roslyn.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Text;

namespace CodeIntelligenceMcp.Roslyn;

public sealed class ReferenceQueries(RoslynWorkspaceIndex index)
{
    public async Task<IReadOnlyList<UsageResult>> FindUsagesAsync(
        string symbolName,
        CancellationToken ct = default)
    {
        if (index.Solution is null)
            return [];

        RoslynWorkspaceIndex.IndexedType? indexed = index.FindIndexedType(symbolName);
        if (indexed is null)
            return [];

        IEnumerable<ReferencedSymbol> references = await SymbolFinder.FindReferencesAsync(
            indexed.Symbol,
            index.Solution,
            ct);

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
    {
        if (index.Solution is null)
            return [];

        RoslynWorkspaceIndex.IndexedType? indexed = index.FindIndexedType(typeName);
        if (indexed is null)
            return [];

        IMethodSymbol? method = indexed.Symbol.GetMembers(methodName)
            .OfType<IMethodSymbol>()
            .FirstOrDefault(m => m.MethodKind == MethodKind.Ordinary);
        if (method is null)
            return [];

        IEnumerable<ReferencedSymbol> refs = await SymbolFinder.FindReferencesAsync(method, index.Solution, ct);

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
                string lineText = await GetLineTextAsync(location.SourceTree, span.StartLinePosition.Line, ct);

                results.Add(new CallerResult(
                    callerType,
                    callerMethod,
                    index.Rel(NormalizeRazorPath(location.SourceTree.FilePath)),
                    lineNumber,
                    lineText));
            }
        }

        return results;
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

            if (indexed.ProjectName.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase))
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
