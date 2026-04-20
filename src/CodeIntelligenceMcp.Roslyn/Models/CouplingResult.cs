namespace CodeIntelligenceMcp.Roslyn.Models;

public record TypeCoupling(
    string TypeName,
    string FilePath,
    int LineNumber,
    int EfferentCoupling,
    IReadOnlyList<string> DependsOn);
