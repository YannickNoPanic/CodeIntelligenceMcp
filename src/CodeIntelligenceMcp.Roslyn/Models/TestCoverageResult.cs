namespace CodeIntelligenceMcp.Roslyn.Models;

public sealed record TestCoverageResult(
    int TotalUseCases,
    int CoveredUseCases,
    double CoveragePercent,
    IReadOnlyList<UncoveredUseCase> Uncovered,
    IReadOnlyList<CoveredUseCase> Covered);

public sealed record UncoveredUseCase(string Name, string Namespace, string FilePath);
public sealed record CoveredUseCase(string Name, string TestClassName, string TestFilePath);
