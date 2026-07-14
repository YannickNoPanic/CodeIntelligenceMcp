using CodeIntelligenceMcp.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CodeIntelligenceMcp.Tests;

// Shared builder for in-memory RoslynWorkspaceIndex instances. Limitation: no Solution,
// so SymbolFinder-based queries (FindUsages/FindCallers) and document-based violation
// rules (core-no-*, dto-in-core, empty-catch, throw-ex, async-over-sync) return empty
// here — those are covered by the integration fixture tests instead.
internal static class TestIndex
{
    public static readonly CleanArchitectureNames CleanArch =
        new("App.Core", "App.Infrastructure", "App.Web");

    public static RoslynWorkspaceIndex Create(params (string ProjectName, string Source)[] sources)
    {
        IEnumerable<(Compilation, string)> compilations = sources
            .GroupBy(s => s.ProjectName)
            .Select(group =>
            {
                IEnumerable<SyntaxTree> trees = group.Select((s, i) =>
                    CSharpSyntaxTree.ParseText(s.Source, path: $"{group.Key}\\File{i}.cs"));

                Compilation compilation = CSharpCompilation.Create(
                    group.Key,
                    trees,
                    [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
                    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

                return (compilation, group.Key);
            });

        return RoslynWorkspaceIndex.CreateForTesting(compilations, CleanArch);
    }
}
