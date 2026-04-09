namespace CodeIntelligenceMcp.Roslyn.Models;

public record StructuralSummary(
    int TotalTypes,
    int Interfaces,
    int UseCases,
    int RazorComponents,
    int? AspFiles,
    int? SqlQueries);

public record PatternSummary(
    StructuralSummary StructuralSummary,
    IReadOnlyList<string> Observations);
