namespace CodeIntelligenceMcp.Roslyn.Models;

public sealed record DiagnosticResult(
    string Id,
    string Severity,
    string Message,
    string FilePath,
    int LineNumber,
    string ProjectName,
    string Category);

public sealed record DiagnosticGroup(
    string Id,
    string Severity,
    string Category,
    int Count,
    IReadOnlyList<DiagnosticResult> Examples);
