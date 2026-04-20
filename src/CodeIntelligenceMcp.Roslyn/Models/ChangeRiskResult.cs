namespace CodeIntelligenceMcp.Roslyn.Models;

public record ChangeRiskResult(
    string TypeName,
    int RiskScore,
    string RiskLabel,
    int ReferencingTypes,
    int Coupling,
    int MaxComplexity,
    bool HasTests,
    IReadOnlyList<string> ReferencedBy,
    IReadOnlyList<MethodComplexity> ComplexMethods,
    string Summary);
