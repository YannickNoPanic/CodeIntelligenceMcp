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
        "missing-cancellation-token" => DetectMissingCancellationToken(),
        "no-async-void" => DetectNoAsyncVoid(),
        "async-over-sync" => DetectAsyncOverSync(),
        "use-case-not-thin" => DetectUseCaseNotThin(),
        "empty-catch" => DetectEmptyCatch(),
        "throw-ex" => DetectThrowEx(),
        "layer-boundary" => DetectLayerBoundary(),
        "too-many-params" => DetectTooManyParams(),
        "blazor-injects-infra" => DetectBlazorInjectsInfra(),
        "services-in-web" => DetectServicesInWeb(),
        "missing-interface" => DetectMissingInterface(),
        "direct-instantiation" => DetectDirectInstantiation(),
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

            if (document.FilePath.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase))
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

            if (document.FilePath.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase))
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

    public IReadOnlyList<ViolationResult> DetectMissingCancellationToken()
    {
        List<ViolationResult> results = [];

        foreach (var (symbol, projectName, filePath, _) in index.QueryTypes())
        {
            if (projectName.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase))
                continue;

            if (symbol.TypeKind == TypeKind.Interface)
                continue;

            foreach (IMethodSymbol method in symbol.GetMembers().OfType<IMethodSymbol>())
            {
                if (method.MethodKind != MethodKind.Ordinary || !method.IsAsync)
                    continue;

                if (method.DeclaredAccessibility != Accessibility.Public)
                    continue;

                if (method.Parameters.Length > 0
                    && method.Parameters[^1].Type.Name == "CancellationToken")
                    continue;

                Location? location = method.Locations.FirstOrDefault(l => l.IsInSource);
                int lineStart = location is not null ? location.GetLineSpan().StartLinePosition.Line + 1 : 0;

                results.Add(new ViolationResult(
                    "missing-cancellation-token",
                    filePath,
                    lineStart,
                    symbol.Name,
                    method.Name,
                    $"Public async method '{symbol.Name}.{method.Name}' is missing a CancellationToken parameter."));
            }
        }

        return results;
    }

    public IReadOnlyList<ViolationResult> DetectNoAsyncVoid()
    {
        List<ViolationResult> results = [];

        foreach (var (symbol, projectName, filePath, _) in index.QueryTypes())
        {
            if (projectName.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (IMethodSymbol method in symbol.GetMembers().OfType<IMethodSymbol>())
            {
                if (method.MethodKind != MethodKind.Ordinary)
                    continue;

                if (!method.IsAsync || !method.ReturnsVoid)
                    continue;

                // Whitelist event handlers: name starts with "On" and first param type ends with "EventArgs"
                if (method.Name.StartsWith("On", StringComparison.Ordinal)
                    && method.Parameters.Length > 0
                    && method.Parameters[0].Type.Name.EndsWith("EventArgs", StringComparison.Ordinal))
                    continue;

                Location? location = method.Locations.FirstOrDefault(l => l.IsInSource);
                int lineStart = location is not null ? location.GetLineSpan().StartLinePosition.Line + 1 : 0;

                results.Add(new ViolationResult(
                    "no-async-void",
                    filePath,
                    lineStart,
                    symbol.Name,
                    method.Name,
                    $"Method '{symbol.Name}.{method.Name}' is async void. Use async Task instead."));
            }
        }

        return results;
    }

    public IReadOnlyList<ViolationResult> DetectUseCaseNotThin()
    {
        List<ViolationResult> results = [];

        foreach (var (symbol, projectName, filePath, lineStart) in index.QueryTypes(
            s => s.TypeKind == TypeKind.Class
                && !s.IsAbstract
                && s.AllInterfaces.Any(i => i.Name.StartsWith("IUseCase", StringComparison.OrdinalIgnoreCase))))
        {
            if (projectName.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase))
                continue;

            bool found = false;
            string violatingMember = string.Empty;
            string violatingType = string.Empty;

            foreach (IMethodSymbol ctor in symbol.Constructors.Where(c => !c.IsStatic))
            {
                if (found) break;
                foreach (IParameterSymbol p in ctor.Parameters)
                {
                    if (!IsEfCoreType(p.Type)) continue;
                    found = true;
                    violatingMember = p.Name;
                    violatingType = p.Type.Name;
                    break;
                }
            }

            if (!found)
            {
                foreach (ISymbol member in symbol.GetMembers())
                {
                    ITypeSymbol? t = member switch
                    {
                        IFieldSymbol f => f.Type,
                        IPropertySymbol p => p.Type,
                        _ => null
                    };
                    if (t is null || !IsEfCoreType(t)) continue;
                    found = true;
                    violatingMember = member.Name;
                    violatingType = t.Name;
                    break;
                }
            }

            if (found)
            {
                results.Add(new ViolationResult(
                    "use-case-not-thin",
                    filePath,
                    lineStart,
                    symbol.Name,
                    violatingMember,
                    $"UseCase '{symbol.Name}' depends directly on EF Core type '{violatingType}'. Use repository/query interfaces instead."));
            }
        }

        return results;
    }

    public IReadOnlyList<ViolationResult> DetectEmptyCatch()
    {
        List<ViolationResult> results = [];

        foreach (Document document in index.GetAllDocuments(skipTests: true))
        {
            if (document.FilePath is null)
                continue;

            SyntaxNode? root = document.GetSyntaxRootAsync().GetAwaiter().GetResult();
            if (root is null)
                continue;

            foreach (CatchClauseSyntax catchClause in root.DescendantNodes().OfType<CatchClauseSyntax>())
            {
                if (catchClause.Block.Statements.Count > 0)
                    continue;

                int lineNumber = catchClause.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

                string typeName = catchClause.Ancestors()
                    .OfType<TypeDeclarationSyntax>()
                    .FirstOrDefault()
                    ?.Identifier.Text ?? "<unknown>";

                string methodName = catchClause.Ancestors()
                    .OfType<MethodDeclarationSyntax>()
                    .FirstOrDefault()
                    ?.Identifier.Text ?? "<unknown>";

                results.Add(new ViolationResult(
                    "empty-catch",
                    document.FilePath,
                    lineNumber,
                    typeName,
                    methodName,
                    $"Empty catch block in '{typeName}.{methodName}'. Swallowed exceptions hide failures."));
            }
        }

        return results;
    }

    public IReadOnlyList<ViolationResult> DetectThrowEx()
    {
        List<ViolationResult> results = [];

        foreach (Document document in index.GetAllDocuments(skipTests: true))
        {
            if (document.FilePath is null)
                continue;

            SyntaxNode? root = document.GetSyntaxRootAsync().GetAwaiter().GetResult();
            if (root is null)
                continue;

            foreach (CatchClauseSyntax catchClause in root.DescendantNodes().OfType<CatchClauseSyntax>())
            {
                string? caughtVariable = catchClause.Declaration?.Identifier.Text;
                if (string.IsNullOrEmpty(caughtVariable))
                    continue;

                foreach (ThrowStatementSyntax throwStatement in catchClause.Block.DescendantNodes().OfType<ThrowStatementSyntax>())
                {
                    if (throwStatement.Expression is not IdentifierNameSyntax identifier)
                        continue;

                    if (!identifier.Identifier.Text.Equals(caughtVariable, StringComparison.Ordinal))
                        continue;

                    int lineNumber = throwStatement.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

                    string typeName = catchClause.Ancestors()
                        .OfType<TypeDeclarationSyntax>()
                        .FirstOrDefault()
                        ?.Identifier.Text ?? "<unknown>";

                    string methodName = catchClause.Ancestors()
                        .OfType<MethodDeclarationSyntax>()
                        .FirstOrDefault()
                        ?.Identifier.Text ?? "<unknown>";

                    results.Add(new ViolationResult(
                        "throw-ex",
                        document.FilePath,
                        lineNumber,
                        typeName,
                        methodName,
                        $"'throw {caughtVariable};' in '{typeName}.{methodName}' loses the original stack trace. Use 'throw;' instead."));
                }
            }
        }

        return results;
    }

    public IReadOnlyList<ViolationResult> DetectBlazorInjectsInfra()
    {
        List<ViolationResult> results = [];

        foreach (string filePath in index.GetRazorFilePaths())
        {
            string[] lines = File.ReadAllLines(filePath);
            for (int i = 0; i < lines.Length; i++)
            {
                string trimmed = lines[i].TrimStart();
                if (!trimmed.StartsWith("@inject", StringComparison.OrdinalIgnoreCase))
                    continue;

                string[] parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2)
                    continue;

                string typeName = parts[1];

                bool isInfra = typeName.Contains("Repository", StringComparison.OrdinalIgnoreCase)
                    || typeName.Contains("Queries", StringComparison.OrdinalIgnoreCase)
                    || typeName.Contains("DbContext", StringComparison.OrdinalIgnoreCase)
                    || typeName.Contains("UnitOfWork", StringComparison.OrdinalIgnoreCase);

                bool isService = typeName.Contains("Service", StringComparison.OrdinalIgnoreCase)
                    && !typeName.Contains("UseCase", StringComparison.OrdinalIgnoreCase);

                if (!isInfra && !isService)
                    continue;

                string componentName = Path.GetFileNameWithoutExtension(filePath);
                string kind = isService ? "service" : "infrastructure type";
                results.Add(new ViolationResult(
                    "blazor-injects-infra",
                    filePath,
                    i + 1,
                    componentName,
                    null,
                    $"Blazor component '{componentName}' injects {kind} '{typeName}' directly. Use a use case instead."));
            }
        }

        return results;
    }

    public IReadOnlyList<ViolationResult> DetectTooManyParams(int maxParams = 5)
    {
        List<ViolationResult> results = [];

        foreach (var (symbol, projectName, filePath, _) in index.QueryTypes())
        {
            if (projectName.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (IMethodSymbol method in symbol.GetMembers().OfType<IMethodSymbol>())
            {
                if (method.MethodKind != MethodKind.Ordinary)
                    continue;

                if (method.IsOverride || method.ExplicitInterfaceImplementations.Length > 0)
                    continue;

                // Skip compiler-synthesized Deconstruct on positional records
                if (method.Name == "Deconstruct" && symbol.IsRecord)
                    continue;

                if (method.Parameters.Length <= maxParams)
                    continue;

                Location? location = method.Locations.FirstOrDefault(l => l.IsInSource);
                int lineStart = location is not null ? location.GetLineSpan().StartLinePosition.Line + 1 : 0;

                string memberFilePath = location?.SourceTree?.FilePath ?? string.Empty;
                if (memberFilePath.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                    || memberFilePath.Contains("/obj/", StringComparison.Ordinal))
                    continue;

                results.Add(new ViolationResult(
                    "too-many-params",
                    filePath,
                    lineStart,
                    symbol.Name,
                    method.Name,
                    $"'{symbol.Name}.{method.Name}' has {method.Parameters.Length} parameters (limit: {maxParams}). Consider a parameter object."));
            }
        }

        return results;
    }

    public IReadOnlyList<ViolationResult> DetectLayerBoundary()
    {
        if (string.IsNullOrEmpty(cleanArch.CoreProject))
            return [];

        List<ViolationResult> results = [];
        ProjectDependency deps = index.GetProjectDependencies();

        foreach (DependencyEdge edge in deps.Dependencies)
        {
            // Skip test projects — they reference what they test by design
            if (edge.From.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase))
                continue;

            bool fromIsCore = string.Equals(edge.From, cleanArch.CoreProject, StringComparison.OrdinalIgnoreCase);
            bool fromIsInfra = !string.IsNullOrEmpty(cleanArch.InfraProject)
                && string.Equals(edge.From, cleanArch.InfraProject, StringComparison.OrdinalIgnoreCase);

            bool toIsInfra = !string.IsNullOrEmpty(cleanArch.InfraProject)
                && string.Equals(edge.To, cleanArch.InfraProject, StringComparison.OrdinalIgnoreCase);
            bool toIsWeb = !string.IsNullOrEmpty(cleanArch.WebProject)
                && string.Equals(edge.To, cleanArch.WebProject, StringComparison.OrdinalIgnoreCase);

            if (fromIsCore && toIsInfra)
                results.Add(new ViolationResult(
                    "layer-boundary",
                    string.Empty,
                    0,
                    edge.From,
                    null,
                    $"Core project '{edge.From}' references Infrastructure '{edge.To}'. Core must not depend on Infrastructure."));

            if (fromIsCore && toIsWeb)
                results.Add(new ViolationResult(
                    "layer-boundary",
                    string.Empty,
                    0,
                    edge.From,
                    null,
                    $"Core project '{edge.From}' references Web '{edge.To}'. Core must not depend on Web."));

            if (fromIsInfra && toIsWeb)
                results.Add(new ViolationResult(
                    "layer-boundary",
                    string.Empty,
                    0,
                    edge.From,
                    null,
                    $"Infrastructure project '{edge.From}' references Web '{edge.To}'. Infrastructure must not depend on Web."));
        }

        return results;
    }

    public IReadOnlyList<ViolationResult> DetectServicesInWeb()
    {
        if (string.IsNullOrEmpty(cleanArch.WebProject))
            return [];

        List<ViolationResult> results = [];

        foreach (var (symbol, projectName, filePath, lineStart) in index.QueryTypes())
        {
            if (!string.Equals(projectName, cleanArch.WebProject, StringComparison.OrdinalIgnoreCase))
                continue;

            string name = symbol.Name;
            bool isService = name.EndsWith("Service", StringComparison.OrdinalIgnoreCase)
                && !name.Contains("UseCase", StringComparison.OrdinalIgnoreCase);
            bool isRepository = name.EndsWith("Repository", StringComparison.OrdinalIgnoreCase);

            if (!isService && !isRepository)
                continue;

            bool isInterface = symbol.TypeKind == TypeKind.Interface;
            string expected = isInterface ? "Core" : "Infrastructure";
            string kind = isInterface ? "interface" : "class";

            results.Add(new ViolationResult(
                "services-in-web",
                filePath,
                lineStart,
                name,
                null,
                $"{char.ToUpperInvariant(kind[0]) + kind[1..]} '{name}' is defined in web project '{projectName}'. It should live in {expected}."));
        }

        return results;
    }

    public IReadOnlyList<ViolationResult> DetectAsyncOverSync()
    {
        List<ViolationResult> results = [];

        foreach (Document doc in index.GetAllDocuments(skipTests: true))
        {
            string? filePath = doc.FilePath;
            if (filePath is null) continue;
            if (filePath.Contains("/obj/", StringComparison.Ordinal)
                || filePath.Contains("\\obj\\", StringComparison.Ordinal))
                continue;

            SyntaxNode? root = doc.GetSyntaxRootAsync().GetAwaiter().GetResult();
            if (root is null) continue;

            foreach (MemberAccessExpressionSyntax memberAccess in root.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
            {
                string name = memberAccess.Name.Identifier.Text;

                // .Result — skip enums/records named XxxResult by checking the parent isn't another member access
                if (name == "Result" && memberAccess.Parent is not MemberAccessExpressionSyntax)
                {
                    int line = memberAccess.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                    results.Add(new ViolationResult(
                        "async-over-sync", filePath, line,
                        memberAccess.Expression.ToString(), null,
                        $"'.Result' blocks the calling thread — use 'await' instead"));
                    continue;
                }

                // .Wait() call
                if (name == "Wait" && memberAccess.Parent is InvocationExpressionSyntax)
                {
                    int line = memberAccess.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                    results.Add(new ViolationResult(
                        "async-over-sync", filePath, line,
                        memberAccess.Expression.ToString(), null,
                        $"'.Wait()' blocks the calling thread — use 'await' instead"));
                    continue;
                }

                // .GetAwaiter().GetResult()
                if (name == "GetResult"
                    && memberAccess.Parent is InvocationExpressionSyntax
                    && memberAccess.Expression is InvocationExpressionSyntax getAwaiterCall
                    && getAwaiterCall.Expression is MemberAccessExpressionSyntax getAwaiterAccess
                    && getAwaiterAccess.Name.Identifier.Text == "GetAwaiter")
                {
                    int line = memberAccess.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                    results.Add(new ViolationResult(
                        "async-over-sync", filePath, line,
                        getAwaiterAccess.Expression.ToString(), null,
                        $"'.GetAwaiter().GetResult()' blocks the calling thread — use 'await' instead"));
                }
            }
        }

        return results;
    }

    public IReadOnlyList<ViolationResult> DetectMissingInterface()
    {
        HashSet<string> interfaceNames = new(
            index.QueryTypes()
                .Where(t => t.Symbol.TypeKind == TypeKind.Interface)
                .Select(t => t.Symbol.Name),
            StringComparer.OrdinalIgnoreCase);

        string[] suffixes = ["Service", "Repository", "UseCase", "Queries", "Query"];
        List<ViolationResult> results = [];

        foreach (var (symbol, projectName, filePath, lineStart) in index.QueryTypes())
        {
            if (symbol.TypeKind != TypeKind.Class)
                continue;
            if (filePath.Contains("/obj/", StringComparison.Ordinal)
                || filePath.Contains("\\obj\\", StringComparison.Ordinal))
                continue;
            if (projectName.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase))
                continue;

            if (symbol.IsAbstract)
                continue;

            string name = symbol.Name;
            if (name.StartsWith("Base", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!suffixes.Any(s => name.EndsWith(s, StringComparison.OrdinalIgnoreCase)))
                continue;

            string expectedInterface = "I" + name;
            if (interfaceNames.Contains(expectedInterface))
                continue;

            results.Add(new ViolationResult(
                "missing-interface",
                filePath,
                lineStart,
                name,
                null,
                $"Class '{name}' has no corresponding '{expectedInterface}' interface — not easily testable via DI"));
        }

        return results;
    }

    public IReadOnlyList<ViolationResult> DetectDirectInstantiation()
    {
        string[] suffixes = ["Service", "Repository", "Queries"];
        List<ViolationResult> results = [];

        foreach (Document doc in index.GetAllDocuments(skipTests: true))
        {
            string? filePath = doc.FilePath;
            if (filePath is null) continue;
            if (filePath.Contains("/obj/", StringComparison.Ordinal)
                || filePath.Contains("\\obj\\", StringComparison.Ordinal))
                continue;

            // Infrastructure is expected to create things
            if (doc.Project.Name.Contains("Infrastructure", StringComparison.OrdinalIgnoreCase))
                continue;

            SyntaxNode? root = doc.GetSyntaxRootAsync().GetAwaiter().GetResult();
            if (root is null) continue;

            foreach (ObjectCreationExpressionSyntax newExpr in root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
            {
                string typeName = newExpr.Type.ToString();
                if (!suffixes.Any(s => typeName.EndsWith(s, StringComparison.OrdinalIgnoreCase)))
                    continue;

                int line = newExpr.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                results.Add(new ViolationResult(
                    "direct-instantiation",
                    filePath,
                    line,
                    typeName,
                    null,
                    $"Direct instantiation of '{typeName}' — use dependency injection instead"));
            }
        }

        return results;
    }

    private static bool IsEfCoreType(ITypeSymbol type) =>
        (type.ContainingNamespace?.ToDisplayString() ?? string.Empty)
            .StartsWith("Microsoft.EntityFrameworkCore", StringComparison.OrdinalIgnoreCase);
}
