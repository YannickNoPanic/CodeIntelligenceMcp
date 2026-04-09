namespace CodeIntelligenceMcp.AspClassic.Models;

public record SqlQueryInfo(
    int LineStart,
    int LineEnd,
    string Operation,
    IReadOnlyList<string> Tables,
    IReadOnlyList<string> Columns,
    string Signature,
    IReadOnlyList<string> Parameters
);
