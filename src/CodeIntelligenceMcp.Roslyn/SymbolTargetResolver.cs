using CodeIntelligenceMcp.Roslyn.Models;
using Microsoft.CodeAnalysis;

namespace CodeIntelligenceMcp.Roslyn;

// Resolves the name a tool caller passes ("Type", "Type.Member", or a bare member name) to
// Roslyn symbols, reporting ambiguity and misses instead of silently returning nothing.
internal static class SymbolTargetResolver
{
    private const int MaxCandidates = 20;
    private const string SearchHint = "Use search_symbol to find the exact name, then pass 'Type' or 'Type.Member'.";

    public static (IReadOnlyList<ISymbol> Symbols, TargetLookup Lookup) ResolveUsageTarget(
        RoslynWorkspaceIndex index,
        string symbolName)
    {
        IReadOnlyList<RoslynWorkspaceIndex.IndexedType> types = index.FindIndexedTypes(symbolName);
        if (types.Count == 1)
            return ([types[0].Symbol], TargetLookup.Ok);
        if (types.Count > 1)
            return ([], Ambiguous(symbolName, [.. types.Select(t => t.Symbol.ToDisplayString())]));

        int dot = symbolName.LastIndexOf('.');
        if (dot > 0)
            return ResolveQualifiedMember(index, symbolName[..dot], symbolName[(dot + 1)..]);

        return ResolveBareMember(index, symbolName);
    }

    public static (IReadOnlyList<IMethodSymbol> Methods, TargetLookup Lookup) ResolveCallerTarget(
        RoslynWorkspaceIndex index,
        string typeName,
        string methodName)
    {
        RoslynWorkspaceIndex.IndexedType? indexed = index.FindIndexedType(typeName);
        if (indexed is null)
            return ([], TargetLookup.NotFound($"type '{typeName}' not found", SearchHint));

        IReadOnlyList<IMethodSymbol> methods = [.. OrdinaryMethods(indexed.Symbol, methodName)];
        if (methods.Count > 0)
            return (methods, TargetLookup.Ok);

        IReadOnlyList<string> available = [.. indexed.Symbol.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(m => m.MethodKind == MethodKind.Ordinary && !m.IsImplicitlyDeclared)
            .Select(m => m.Name)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Take(MaxCandidates)];

        return ([], TargetLookup.NotFound(
            $"method '{methodName}' not found on '{typeName}'",
            "Candidates lists the methods declared on the type.",
            available));
    }

    private static (IReadOnlyList<ISymbol>, TargetLookup) ResolveQualifiedMember(
        RoslynWorkspaceIndex index,
        string typeName,
        string memberName)
    {
        IReadOnlyList<RoslynWorkspaceIndex.IndexedType> owners = index.FindIndexedTypes(typeName);
        if (owners.Count > 1)
            return ([], Ambiguous(typeName, [.. owners.Select(t => t.Symbol.ToDisplayString())]));

        IReadOnlyList<ISymbol> members = owners.Count == 1 ? UsableMembers(owners[0].Symbol, memberName) : [];
        return members.Count > 0
            ? (members, TargetLookup.Ok)
            : ([], TargetLookup.NotFound($"symbol '{typeName}.{memberName}' not found", SearchHint));
    }

    private static (IReadOnlyList<ISymbol>, TargetLookup) ResolveBareMember(RoslynWorkspaceIndex index, string memberName)
    {
        List<(INamedTypeSymbol Owner, IReadOnlyList<ISymbol> Members)> owners = [.. index.AllTypes
            .Select(t => (Owner: t.Symbol, Members: UsableMembers(t.Symbol, memberName)))
            .Where(o => o.Members.Count > 0)
            .DistinctBy(o => o.Owner.ToDisplayString())];

        if (owners.Count == 1)
            return (owners[0].Members, TargetLookup.Ok);
        if (owners.Count > 1)
            return ([], Ambiguous(memberName, [.. owners.Select(o => $"{o.Owner.ToDisplayString()}.{memberName}")]));

        return ([], TargetLookup.NotFound($"symbol '{memberName}' not found", SearchHint));
    }

    private static IReadOnlyList<ISymbol> UsableMembers(INamedTypeSymbol type, string name) =>
        [.. type.GetMembers(name).Where(m => !m.IsImplicitlyDeclared && m is IMethodSymbol { MethodKind: MethodKind.Ordinary }
            or IPropertySymbol or IFieldSymbol or IEventSymbol)];

    private static IEnumerable<IMethodSymbol> OrdinaryMethods(INamedTypeSymbol type, string name) =>
        type.GetMembers(name).OfType<IMethodSymbol>().Where(m => m.MethodKind == MethodKind.Ordinary);

    private static TargetLookup Ambiguous(string name, IReadOnlyList<string> candidates) =>
        TargetLookup.NotFound(
            $"ambiguous name '{name}' — pass one of the candidates",
            "Use a fully qualified 'Namespace.Type.Member'.",
            [.. candidates.Order(StringComparer.Ordinal).Take(MaxCandidates)]);
}
