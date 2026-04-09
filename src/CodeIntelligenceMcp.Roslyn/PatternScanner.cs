using CodeIntelligenceMcp.Roslyn.Models;

namespace CodeIntelligenceMcp.Roslyn;

public sealed class PatternScanner(RoslynWorkspaceIndex index, CleanArchitectureNames cleanArch)
{
    public PatternSummary Scan()
    {
        ViolationDetector detector = new(index, cleanArch);

        int totalTypes = index.TotalTypeCount;

        int interfaces = index.FindTypes(kind: "interface").Count;

        int useCases = index.FindTypes(implementsInterface: "IUseCase")
            .Count(t => t.Kind != "interface");

        int razorComponents = index.CountRazorDocuments();

        IReadOnlyList<ViolationResult> usecaseNotSealed = detector.DetectUsecaseNotSealed();
        IReadOnlyList<ViolationResult> dtoInCore = detector.DetectDtoInCore();
        IReadOnlyList<ViolationResult> controllerNotThin = detector.DetectControllerNotThin();
        IReadOnlyList<ViolationResult> jsonInView = detector.DetectJsonParsingInView();
        IReadOnlyList<ViolationResult> inlineViewmodel = detector.DetectInlineViewModelRazor();
        IReadOnlyList<ViolationResult> coreNoEf = detector.DetectCoreNoEf();
        IReadOnlyList<ViolationResult> coreNoHttp = detector.DetectCoreNoHttp();
        IReadOnlyList<ViolationResult> coreNoAzure = detector.DetectCoreNoAzure();
        IReadOnlyList<ViolationResult> businessLogic = detector.DetectBusinessLogicInRazor();

        int sealedCount = useCases - usecaseNotSealed.Count;
        int notSealedCount = usecaseNotSealed.Count;

        List<string> observations = [];

        observations.Add($"IUseCase implementations: {sealedCount} sealed, {notSealedCount} not sealed");
        observations.Add($"Types with Dto suffix in Core: {dtoInCore.Count}");
        observations.Add($"Controller actions exceeding 10 lines: {controllerNotThin.Count}");
        observations.Add($"JsonDocument/JsonSerializer usage in .razor files: {jsonInView.Count}");
        observations.Add($"Inline class definitions in .razor @code blocks: {inlineViewmodel.Count}");

        AddViolationObservation(observations, "core-no-ef", coreNoEf);
        AddViolationObservation(observations, "core-no-http", coreNoHttp);
        AddViolationObservation(observations, "core-no-azure", coreNoAzure);
        AddViolationObservation(observations, "business-logic-in-razor", businessLogic);

        StructuralSummary structural = new(
            TotalTypes: totalTypes,
            Interfaces: interfaces,
            UseCases: useCases,
            RazorComponents: razorComponents,
            AspFiles: null,
            SqlQueries: null);

        return new PatternSummary(structural, observations);
    }

    private static void AddViolationObservation(
        List<string> observations,
        string rule,
        IReadOnlyList<ViolationResult> violations)
    {
        if (violations.Count > 0)
            observations.Add($"{rule}: {violations.Count} violation(s)");
    }
}
