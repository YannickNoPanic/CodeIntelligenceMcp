using CodeIntelligenceMcp.Roslyn.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeIntelligenceMcp.Roslyn;

public sealed class ComplexityAnalyzer(RoslynWorkspaceIndex index)
{
    public IReadOnlyList<MethodComplexity> Analyze(
        int minComplexity = 5,
        string? projectFilter = null,
        int minLines = 0,
        string sortBy = "complexity",
        string? typeFilter = null)
    {
        List<MethodComplexity> results = [];

        foreach (Document document in index.GetAllDocuments(skipTests: true))
        {
            if (projectFilter is not null
                && !document.Project.Name.Contains(projectFilter, StringComparison.OrdinalIgnoreCase))
                continue;

            string? filePath = document.FilePath;
            if (filePath is null)
                continue;

            SyntaxNode? root = document.GetSyntaxRootAsync().GetAwaiter().GetResult();
            if (root is null)
                continue;

            foreach (MethodDeclarationSyntax method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                SyntaxNode? body = (SyntaxNode?)method.Body ?? method.ExpressionBody;
                if (body is null)
                    continue;

                string typeName = method.Ancestors()
                    .OfType<TypeDeclarationSyntax>()
                    .FirstOrDefault()
                    ?.Identifier.Text ?? "<unknown>";

                if (typeFilter is not null && !typeName.Equals(typeFilter, StringComparison.OrdinalIgnoreCase))
                    continue;

                FileLinePositionSpan methodSpan = method.GetLocation().GetLineSpan();
                int lineNumber = methodSpan.StartLinePosition.Line + 1;
                int lines = methodSpan.EndLinePosition.Line - methodSpan.StartLinePosition.Line + 1;
                int complexity = ComputeComplexity(body);

                if (complexity < minComplexity && (minLines == 0 || lines < minLines))
                    continue;

                results.Add(new MethodComplexity(
                    typeName,
                    method.Identifier.Text,
                    filePath,
                    lineNumber,
                    complexity,
                    GetLabel(complexity),
                    lines));
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

                if (complexity < minComplexity && (minLines == 0 || lines < minLines))
                    continue;

                results.Add(new MethodComplexity(
                    typeName,
                    ".ctor",
                    filePath,
                    lineNumber,
                    complexity,
                    GetLabel(complexity),
                    lines));
            }
        }

        return sortBy == "lines"
            ? [.. results.OrderByDescending(r => r.Lines)]
            : [.. results.OrderByDescending(r => r.Complexity)];
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
