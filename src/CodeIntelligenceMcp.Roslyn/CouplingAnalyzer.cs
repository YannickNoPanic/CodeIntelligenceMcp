using CodeIntelligenceMcp.Roslyn.Models;
using Microsoft.CodeAnalysis;

namespace CodeIntelligenceMcp.Roslyn;

public sealed class CouplingAnalyzer(RoslynWorkspaceIndex index)
{
    public IReadOnlyList<TypeCoupling> GetCoupling(string? projectFilter = null, int minCoupling = 5)
    {
        List<TypeCoupling> results = [];

        foreach (RoslynWorkspaceIndex.IndexedType indexed in index.AllTypes)
        {
            if (indexed.ProjectName.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase))
                continue;

            if (projectFilter is not null
                && !indexed.ProjectName.Contains(projectFilter, StringComparison.OrdinalIgnoreCase))
                continue;

            HashSet<string> dependsOn = ComputeCoupling(indexed.Symbol);

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

    internal static HashSet<string> ComputeCoupling(INamedTypeSymbol symbol)
    {
        HashSet<string> dependsOn = new(StringComparer.Ordinal);

        foreach (ISymbol member in symbol.GetMembers())
        {
            IEnumerable<ITypeSymbol> types = member switch
            {
                IFieldSymbol f => [f.Type],
                IPropertySymbol p => [p.Type],
                IMethodSymbol m => [.. m.Parameters.Select(p => p.Type), m.ReturnType],
                _ => []
            };

            foreach (ITypeSymbol t in types)
                CollectExternalTypes(t, symbol, dependsOn);
        }

        return dependsOn;
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
}
