using CodeIntelligenceMcp.Roslyn.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeIntelligenceMcp.Roslyn;

public sealed class ViolationDetector(RoslynWorkspaceIndex index, CleanArchitectureNames cleanArch)
{
    public IReadOnlyList<ViolationResult> Detect(string rule) => rule switch
    {
        "core-no-ef" => DetectCoreNoEf(),
        "core-no-http" => DetectCoreNoHttp(),
        "core-no-azure" => DetectCoreNoAzure(),
        "usecase-not-sealed" => DetectUsecaseNotSealed(),
        "inline-viewmodel-razor" => DetectInlineViewModelRazor(),
        "business-logic-in-razor" => DetectBusinessLogicInRazor(),
        "json-parsing-in-view" => DetectJsonParsingInView(),
        "controller-not-thin" => DetectControllerNotThin(),
        "dto-in-core" => DetectDtoInCore(),
        _ => throw new ArgumentException($"Unknown rule: {rule}", nameof(rule))
    };

    public IReadOnlyList<ViolationResult> DetectCoreNoEf()
    {
        return DetectForbiddenUsing(
            "core-no-ef",
            cleanArch.CoreProject,
            ns => ns.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.OrdinalIgnoreCase),
            ns => $"Core project must not reference EF Core. Found: using {ns}");
    }

    public IReadOnlyList<ViolationResult> DetectCoreNoHttp()
    {
        return DetectForbiddenUsing(
            "core-no-http",
            cleanArch.CoreProject,
            ns => ns.Equals("System.Net.Http", StringComparison.OrdinalIgnoreCase)
                || ns.Contains("IHttpClientFactory", StringComparison.OrdinalIgnoreCase),
            ns => $"Core project must not reference HTTP types. Found: using {ns}");
    }

    public IReadOnlyList<ViolationResult> DetectCoreNoAzure()
    {
        return DetectForbiddenUsing(
            "core-no-azure",
            cleanArch.CoreProject,
            ns => ns.StartsWith("Azure.", StringComparison.OrdinalIgnoreCase)
                || ns.StartsWith("Microsoft.Azure.", StringComparison.OrdinalIgnoreCase),
            ns => $"Core project must not reference Azure SDK. Found: using {ns}");
    }

    private IReadOnlyList<ViolationResult> DetectForbiddenUsing(
        string rule,
        string projectName,
        Func<string, bool> isForbidden,
        Func<string, string> descriptionFactory)
    {
        List<ViolationResult> results = [];

        foreach (Document document in index.GetProjectDocuments(projectName))
        {
            if (document.FilePath is null)
                continue;

            SyntaxNode? root = document.GetSyntaxRootAsync().GetAwaiter().GetResult();
            if (root is null)
                continue;

            foreach (UsingDirectiveSyntax usingDirective in root.DescendantNodes().OfType<UsingDirectiveSyntax>())
            {
                string ns = usingDirective.Name?.ToString() ?? string.Empty;
                if (!isForbidden(ns))
                    continue;

                int lineNumber = usingDirective.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                results.Add(new ViolationResult(
                    rule,
                    document.FilePath,
                    lineNumber,
                    null,
                    null,
                    descriptionFactory(ns)));
            }
        }

        return results;
    }

    public IReadOnlyList<ViolationResult> DetectUsecaseNotSealed()
    {
        List<ViolationResult> results = [];

        foreach (var (name, ns, filePath, lineStart, isSealed, isInterface) in index.QueryTypesSealedStatus(
            symbol => symbol.AllInterfaces.Any(i =>
                i.Name.StartsWith("IUseCase", StringComparison.OrdinalIgnoreCase))))
        {
            if (isInterface || isSealed)
                continue;

            results.Add(new ViolationResult(
                "usecase-not-sealed",
                filePath,
                lineStart,
                name,
                null,
                $"Type '{name}' implements IUseCase but is not sealed."));
        }

        return results;
    }

    public IReadOnlyList<ViolationResult> DetectInlineViewModelRazor()
    {
        List<ViolationResult> results = [];

        foreach (Document document in index.GetRazorDocuments())
        {
            if (document.FilePath is null)
                continue;

            BlazorCodeBlock? codeBlock = BlazorFilePreprocessor.ExtractCodeBlock(document.FilePath);
            if (codeBlock is null)
                continue;

            SyntaxTree tree = CSharpSyntaxTree.ParseText(codeBlock.Source);
            SyntaxNode root = tree.GetRoot();

            foreach (ClassDeclarationSyntax classDecl in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                int lineInBlock = classDecl.GetLocation().GetLineSpan().StartLinePosition.Line;
                int lineInFile = codeBlock.LineOffset + lineInBlock;

                results.Add(new ViolationResult(
                    "inline-viewmodel-razor",
                    document.FilePath,
                    lineInFile,
                    classDecl.Identifier.Text,
                    null,
                    $"Inline class '{classDecl.Identifier.Text}' defined inside @code block in Razor component."));
            }
        }

        return results;
    }

    public IReadOnlyList<ViolationResult> DetectBusinessLogicInRazor()
    {
        List<ViolationResult> results = [];

        string[] linqProjectionMethods = ["SelectMany", "GroupBy", "ToDictionary"];

        foreach (Document document in index.GetRazorDocuments())
        {
            if (document.FilePath is null)
                continue;

            BlazorCodeBlock? codeBlock = BlazorFilePreprocessor.ExtractCodeBlock(document.FilePath);
            if (codeBlock is null)
                continue;

            SyntaxTree tree = CSharpSyntaxTree.ParseText(codeBlock.Source);
            SyntaxNode root = tree.GetRoot();

            foreach (InvocationExpressionSyntax invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                string expressionText = invocation.Expression.ToString();

                bool isUseCaseCall = expressionText.Contains("UseCase", StringComparison.OrdinalIgnoreCase);

                if (isUseCaseCall)
                {
                    int lineInBlock = invocation.GetLocation().GetLineSpan().StartLinePosition.Line;
                    int lineInFile = codeBlock.LineOffset + lineInBlock;

                    results.Add(new ViolationResult(
                        "business-logic-in-razor",
                        document.FilePath,
                        lineInFile,
                        null,
                        null,
                        $"Direct use case invocation '{expressionText}' in Razor component @code block."));
                    continue;
                }

                string? methodName = linqProjectionMethods.FirstOrDefault(m =>
                    expressionText.EndsWith("." + m, StringComparison.OrdinalIgnoreCase)
                    || expressionText.Equals(m, StringComparison.OrdinalIgnoreCase));

                if (methodName is null)
                    continue;

                FileLinePositionSpan span = invocation.GetLocation().GetLineSpan();
                int spanLines = span.EndLinePosition.Line - span.StartLinePosition.Line + 1;

                if (spanLines > 10)
                {
                    int lineInFile = codeBlock.LineOffset + span.StartLinePosition.Line;

                    results.Add(new ViolationResult(
                        "business-logic-in-razor",
                        document.FilePath,
                        lineInFile,
                        null,
                        null,
                        $"LINQ projection '{methodName}' spanning {spanLines} lines in Razor component @code block."));
                }
            }
        }

        return results;
    }

    public IReadOnlyList<ViolationResult> DetectJsonParsingInView()
    {
        List<ViolationResult> results = [];

        string[] forbiddenIdentifiers = ["JsonDocument", "JsonSerializer"];

        foreach (Document document in index.GetRazorDocuments())
        {
            if (document.FilePath is null)
                continue;

            BlazorCodeBlock? codeBlock = BlazorFilePreprocessor.ExtractCodeBlock(document.FilePath);
            if (codeBlock is null)
                continue;

            SyntaxTree tree = CSharpSyntaxTree.ParseText(codeBlock.Source);
            SyntaxNode root = tree.GetRoot();

            foreach (IdentifierNameSyntax identifier in root.DescendantNodes().OfType<IdentifierNameSyntax>())
            {
                string name = identifier.Identifier.Text;
                if (!forbiddenIdentifiers.Contains(name, StringComparer.Ordinal))
                    continue;

                int lineInBlock = identifier.GetLocation().GetLineSpan().StartLinePosition.Line;
                int lineInFile = codeBlock.LineOffset + lineInBlock;

                results.Add(new ViolationResult(
                    "json-parsing-in-view",
                    document.FilePath,
                    lineInFile,
                    null,
                    null,
                    $"'{name}' used in Razor component @code block. JSON parsing belongs in infrastructure or use cases."));
            }
        }

        return results;
    }

    public IReadOnlyList<ViolationResult> DetectControllerNotThin()
    {
        List<ViolationResult> results = [];

        IReadOnlyList<TypeSummary> controllers = index.FindTypes(nameContains: "Controller", kind: "class");

        foreach (TypeSummary controller in controllers)
        {
            if (!controller.Name.EndsWith("Controller", StringComparison.OrdinalIgnoreCase))
                continue;

            Models.TypeInfo? typeInfo = index.GetType(controller.Name);
            if (typeInfo is null)
                continue;

            foreach (MethodSummary method in typeInfo.Methods)
            {
                Models.MethodInfo? methodInfo = index.GetMethod(controller.Name, method.Name);
                if (methodInfo is null || string.IsNullOrEmpty(methodInfo.Body))
                    continue;

                SyntaxTree tree = CSharpSyntaxTree.ParseText(methodInfo.Body);
                SyntaxNode root = tree.GetRoot();

                int statementCount = root.DescendantNodes()
                    .OfType<BlockSyntax>()
                    .FirstOrDefault()
                    ?.Statements.Count ?? 0;

                if (statementCount > 10)
                {
                    results.Add(new ViolationResult(
                        "controller-not-thin",
                        controller.FilePath,
                        method.LineStart,
                        controller.Name,
                        method.Name,
                        $"Controller action '{controller.Name}.{method.Name}' has {statementCount} statements (limit: 10)."));
                }
            }
        }

        return results;
    }

    public IReadOnlyList<ViolationResult> DetectDtoInCore()
    {
        List<ViolationResult> results = [];

        foreach (Document document in index.GetProjectDocuments(cleanArch.CoreProject))
        {
            if (document.FilePath is null)
                continue;

            SyntaxNode? root = document.GetSyntaxRootAsync().GetAwaiter().GetResult();
            if (root is null)
                continue;

            foreach (TypeDeclarationSyntax typeDecl in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                string name = typeDecl.Identifier.Text;
                if (!name.EndsWith("Dto", StringComparison.OrdinalIgnoreCase))
                    continue;

                int lineNumber = typeDecl.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

                results.Add(new ViolationResult(
                    "dto-in-core",
                    document.FilePath,
                    lineNumber,
                    name,
                    null,
                    $"Type '{name}' with Dto suffix found in Core project. DTOs are a presentation concern."));
            }
        }

        return results;
    }
}
