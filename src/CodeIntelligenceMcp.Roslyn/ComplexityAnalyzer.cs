using CodeIntelligenceMcp.Roslyn.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeIntelligenceMcp.Roslyn;

internal sealed record ProjectMethodComplexity(string ProjectName, MethodComplexity Method);

public sealed class ComplexityAnalyzer(RoslynWorkspaceIndex index)
{
    public async Task<IReadOnlyList<MethodComplexity>> AnalyzeAsync(
        int minComplexity = 5,
        string? projectFilter = null,
        int minLines = 0,
        string sortBy = "complexity",
        string? typeFilter = null,
        CancellationToken ct = default)
    {
        IReadOnlyList<ProjectMethodComplexity> all = await index.GetAllComplexityAsync(ct);

        IEnumerable<ProjectMethodComplexity> query = all;

        if (projectFilter is not null)
            query = query.Where(x => x.ProjectName.Contains(projectFilter, StringComparison.OrdinalIgnoreCase));

        if (typeFilter is not null)
            query = query.Where(x => x.Method.TypeName.Equals(typeFilter, StringComparison.OrdinalIgnoreCase));

        query = query.Where(x =>
            x.Method.Complexity >= minComplexity
            || (minLines > 0 && x.Method.Lines >= minLines));

        IEnumerable<MethodComplexity> results = query.Select(x => x.Method);

        return sortBy == "lines"
            ? [.. results.OrderByDescending(r => r.Lines)]
            : [.. results.OrderByDescending(r => r.Complexity)];
    }

    // Full-solution computation, run once per index instance and cached there.
    internal static async Task<IReadOnlyList<ProjectMethodComplexity>> ComputeAllAsync(
        RoslynWorkspaceIndex index,
        CancellationToken ct)
    {
        List<ProjectMethodComplexity> results = [];

        foreach (Document document in index.GetAllDocuments(skipTests: true))
        {
            string? filePath = document.FilePath is { } p ? index.Rel(p) : null;
            if (filePath is null)
                continue;

            SyntaxNode? root = await document.GetSyntaxRootAsync(ct);
            if (root is null)
                continue;

            string projectName = document.Project.Name;

            foreach (MethodDeclarationSyntax method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                SyntaxNode? body = (SyntaxNode?)method.Body ?? method.ExpressionBody;
                if (body is null)
                    continue;

                string typeName = method.Ancestors()
                    .OfType<TypeDeclarationSyntax>()
                    .FirstOrDefault()
                    ?.Identifier.Text ?? "<unknown>";

                FileLinePositionSpan methodSpan = method.GetLocation().GetLineSpan();
                int lineNumber = methodSpan.StartLinePosition.Line + 1;
                int lines = methodSpan.EndLinePosition.Line - methodSpan.StartLinePosition.Line + 1;
                int complexity = ComputeComplexity(body);

                results.Add(new ProjectMethodComplexity(projectName, new MethodComplexity(
                    typeName,
                    method.Identifier.Text,
                    filePath,
                    lineNumber,
                    complexity,
                    GetLabel(complexity),
                    lines)));
            }

            foreach (ConstructorDeclarationSyntax ctor in root.DescendantNodes().OfType<ConstructorDeclarationSyntax>())
            {
                if (ctor.Body is null)
                    continue;

                string typeName = ctor.Ancestors()
                    .OfType<TypeDeclarationSyntax>()
                    .FirstOrDefault()
                    ?.Identifier.Text ?? "<unknown>";

                FileLinePositionSpan ctorSpan = ctor.GetLocation().GetLineSpan();
                int lineNumber = ctorSpan.StartLinePosition.Line + 1;
                int lines = ctorSpan.EndLinePosition.Line - ctorSpan.StartLinePosition.Line + 1;
                int complexity = ComputeComplexity(ctor.Body);

                results.Add(new ProjectMethodComplexity(projectName, new MethodComplexity(
                    typeName,
                    ".ctor",
                    filePath,
                    lineNumber,
                    complexity,
                    GetLabel(complexity),
                    lines)));
            }
        }

        return results;
    }

    private static int ComputeComplexity(SyntaxNode body)
    {
        int complexity = 1;

        foreach (SyntaxNode node in body.DescendantNodes())
        {
            complexity += node switch
            {
                IfStatementSyntax => 1,
                WhileStatementSyntax => 1,
                ForStatementSyntax => 1,
                ForEachStatementSyntax => 1,
                CaseSwitchLabelSyntax => 1,
                CasePatternSwitchLabelSyntax => 1,
                CatchClauseSyntax => 1,
                ConditionalExpressionSyntax => 1,
                BinaryExpressionSyntax binary when
                    binary.IsKind(SyntaxKind.LogicalAndExpression) ||
                    binary.IsKind(SyntaxKind.LogicalOrExpression) ||
                    binary.IsKind(SyntaxKind.CoalesceExpression) => 1,
                SwitchExpressionArmSyntax arm when
                    arm.Pattern is not DiscardPatternSyntax => 1,
                _ => 0
            };
        }

        return complexity;
    }

    private static string GetLabel(int complexity) => complexity switch
    {
        <= 4 => "simple",
        <= 7 => "moderate",
        <= 10 => "complex",
        _ => "very-complex"
    };
}
