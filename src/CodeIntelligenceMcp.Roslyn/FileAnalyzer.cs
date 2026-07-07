using CodeIntelligenceMcp.Roslyn.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeIntelligenceMcp.Roslyn;

public static class FileAnalyzer
{
    public static FileAnalysis Analyze(string fullPath, CleanArchitectureNames cleanArch)
    {
        string extension = Path.GetExtension(fullPath).ToLowerInvariant();
        string fileType = extension == ".razor" ? "razor" : "cs";

        List<FileObservation> observations = fileType == "razor"
            ? AnalyzeRazor(fullPath)
            : AnalyzeCs(fullPath, cleanArch);

        return new FileAnalysis(fullPath, fileType, observations);
    }

    private static List<FileObservation> AnalyzeRazor(string fullPath)
    {
        List<FileObservation> observations = [];

        BlazorCodeBlock? codeBlock = BlazorFilePreprocessor.ExtractCodeBlock(fullPath);
        if (codeBlock is null)
            return observations;

        SyntaxTree tree = CSharpSyntaxTree.ParseText(codeBlock.Source);
        SyntaxNode root = tree.GetRoot();

        foreach (ClassDeclarationSyntax classDecl in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
        {
            string name = classDecl.Identifier.Text;
            observations.Add(new FileObservation(
                "inline-type",
                name,
                $"Class '{name}' defined inline in @code block"));
        }

        string[] projectionMethods = ["SelectMany", "GroupBy", "ToDictionary"];

        foreach (InvocationExpressionSyntax invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            string expr = invocation.Expression.ToString();

            if (expr.Contains("UseCase", StringComparison.OrdinalIgnoreCase))
            {
                int line = codeBlock.LineOffset + invocation.GetLocation().GetLineSpan().StartLinePosition.Line;
                observations.Add(new FileObservation(
                    "business-logic-in-view",
                    $"line {line}",
                    $"Use case invoked directly: {expr}"));
                continue;
            }

            string? matchedMethod = projectionMethods.FirstOrDefault(m =>
                expr.EndsWith("." + m, StringComparison.OrdinalIgnoreCase)
                || expr.Equals(m, StringComparison.OrdinalIgnoreCase));

            if (matchedMethod is not null)
            {
                int line = codeBlock.LineOffset + invocation.GetLocation().GetLineSpan().StartLinePosition.Line;
                observations.Add(new FileObservation(
                    "data-assembly-in-component",
                    $"line {line}",
                    $"LINQ projection '{matchedMethod}' in component"));
            }
        }

        string[] jsonIdentifiers = ["JsonDocument", "JsonSerializer"];
        foreach (IdentifierNameSyntax identifier in root.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            string name = identifier.Identifier.Text;
            if (!jsonIdentifiers.Contains(name, StringComparer.Ordinal))
                continue;

            int line = codeBlock.LineOffset + identifier.GetLocation().GetLineSpan().StartLinePosition.Line;
            observations.Add(new FileObservation(
                "json-parsing-in-view",
                $"line {line}",
                $"'{name}' used in component"));
        }

        AddMissingCancellationTokenObservations(root, observations);

        return observations;
    }

    private static List<FileObservation> AnalyzeCs(string fullPath, CleanArchitectureNames cleanArch)
    {
        List<FileObservation> observations = [];

        string source = File.ReadAllText(fullPath);
        SyntaxTree tree = CSharpSyntaxTree.ParseText(source);
        SyntaxNode root = tree.GetRoot();

        AddMissingCancellationTokenObservations(root, observations);

        if (string.IsNullOrEmpty(cleanArch.CoreProject) || !IsInLayer(fullPath, cleanArch.CoreProject))
            return observations;

        foreach (UsingDirectiveSyntax usingDirective in root.DescendantNodes().OfType<UsingDirectiveSyntax>())
        {
            string ns = usingDirective.Name?.ToString() ?? string.Empty;

            string? violation = ns switch
            {
                _ when ForbiddenCoreNamespaces.IsEfCore(ns) => "Core project must not reference EF Core",
                _ when ForbiddenCoreNamespaces.IsHttp(ns) => "Core project must not reference HTTP types",
                _ when ForbiddenCoreNamespaces.IsAzure(ns) => "Core project must not reference Azure SDK",
                _ => null
            };

            if (violation is null)
                continue;

            int lineNumber = usingDirective.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            observations.Add(new FileObservation(
                "layer-violation",
                $"line {lineNumber}",
                $"{violation}. Found: using {ns}"));
        }

        return observations;
    }

    private static void AddMissingCancellationTokenObservations(SyntaxNode root, List<FileObservation> observations)
    {
        foreach (MethodDeclarationSyntax method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            bool isPublic = method.Modifiers.Any(m => m.Text == "public");
            bool isAsync = method.Modifiers.Any(m => m.Text == "async");
            if (!isPublic || !isAsync)
                continue;

            bool hasCancellationToken = method.ParameterList.Parameters.Any(p =>
                p.Type?.ToString().Contains("CancellationToken", StringComparison.Ordinal) == true);

            if (!hasCancellationToken)
            {
                string methodName = method.Identifier.Text;
                observations.Add(new FileObservation(
                    "missing-cancellation-token",
                    methodName,
                    $"Public async method '{methodName}' has no CancellationToken parameter"));
            }
        }
    }

    private static bool IsInLayer(string fullPath, string projectName)
    {
        string normalizedPath = fullPath.Replace('\\', '/');
        return normalizedPath.Contains("/" + projectName + "/", StringComparison.OrdinalIgnoreCase);
    }
}
