using CodeIntelligenceMcp.Roslyn.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;

namespace CodeIntelligenceMcp.Roslyn;

public sealed class RiskAnalyzer(RoslynWorkspaceIndex index)
{
    public async Task<ChangeRiskResult?> GetChangeRiskAsync(string typeName, CancellationToken ct = default)
    {
        if (index.Solution is null)
            return null;

        RoslynWorkspaceIndex.IndexedType? indexed = index.FindIndexedType(typeName);
        if (indexed is null)
            return null;

        // 1. Referencing types — how many distinct types reference this one
        IEnumerable<ReferencedSymbol> refs = await SymbolFinder.FindReferencesAsync(indexed.Symbol, index.Solution, index.ScopedDocuments, ct);

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
        HashSet<string> couplingSet = CouplingAnalyzer.ComputeCoupling(indexed.Symbol);

        // 3. Complexity — scoped to this type only, from the cached full-solution analysis
        ComplexityAnalyzer complexityAnalyzer = new(index);
        IReadOnlyList<MethodComplexity> allMethods = await complexityAnalyzer.AnalyzeAsync(
            minComplexity: 1, typeFilter: indexed.Symbol.Name, ct: ct);
        int maxComplexity = allMethods.Count > 0 ? allMethods.Max(m => m.Complexity) : 1;
        IReadOnlyList<MethodComplexity> hotspots = [.. allMethods
            .Where(m => m.Complexity >= 5)
            .OrderByDescending(m => m.Complexity)];

        // 4. Tests — convention: XxxTests class in a .Tests project
        bool hasTests = index.TestClassNames.Contains(indexed.Symbol.Name + "Tests");

        // 5. Score
        int referencingCount = referencingTypeNames.Count;
        int coupling = couplingSet.Count;

        int refScore = referencingCount switch { 0 => 0, <= 3 => 10, <= 10 => 20, _ => 30 };
        int couplingScore = CouplingScore(coupling);
        int complexityScore = ComplexityScore(maxComplexity);
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

    public async Task<IReadOnlyList<HotspotResult>> GetHotspotsAsync(
        int topN = 20,
        string? projectFilter = null,
        CancellationToken ct = default)
    {
        ComplexityAnalyzer complexityAnalyzer = new(index);
        IReadOnlyList<MethodComplexity> allComplexity = await complexityAnalyzer.AnalyzeAsync(
            minComplexity: 1, projectFilter: projectFilter, ct: ct);

        Dictionary<string, int> maxByType = allComplexity
            .GroupBy(m => m.TypeName)
            .ToDictionary(g => g.Key, g => g.Max(m => m.Complexity), StringComparer.Ordinal);

        List<HotspotResult> results = [];

        foreach (RoslynWorkspaceIndex.IndexedType indexed in index.AllTypes)
        {
            ct.ThrowIfCancellationRequested();

            if (index.IsTestProject(indexed.ProjectName))
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

            int coupling = CouplingAnalyzer.ComputeCoupling(indexed.Symbol).Count;
            int maxComplexity = maxByType.GetValueOrDefault(indexed.Symbol.Name, 1);
            bool hasTests = index.TestClassNames.Contains(indexed.Symbol.Name + "Tests");

            int score = CouplingScore(coupling) + ComplexityScore(maxComplexity) + (hasTests ? 0 : 20);

            if (score == 0)
                continue;

            List<string> reasons = [];
            if (coupling > 9) reasons.Add($"coupling {coupling}");
            if (maxComplexity >= 8) reasons.Add($"complexity {maxComplexity}");
            if (!hasTests) reasons.Add("no tests");
            string reason = string.Join(", ", reasons);

            results.Add(new HotspotResult(
                indexed.Symbol.Name,
                index.Rel(indexed.FilePath),
                indexed.LineStart,
                score,
                coupling,
                maxComplexity,
                hasTests,
                reason));
        }

        return [.. results.OrderByDescending(r => r.HotspotScore).Take(topN)];
    }

    private static int CouplingScore(int coupling) =>
        coupling switch { <= 4 => 0, <= 9 => 8, <= 14 => 17, _ => 25 };

    private static int ComplexityScore(int maxComplexity) =>
        maxComplexity switch { <= 4 => 0, <= 9 => 8, <= 14 => 17, _ => 25 };
}
