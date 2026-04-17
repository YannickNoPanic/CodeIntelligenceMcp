namespace CodeIntelligenceMcp.Roslyn.Models;

public sealed record SignatureChange(
    string TypeName,
    string MemberName,
    string? BeforeSignature,
    string? AfterSignature,
    string ChangeKind);

public sealed record ChangedFileDetail(
    string FilePath,
    string Status,
    IReadOnlyList<string> AffectedTypes);

public sealed record ChangeSummary(
    int ChangedFiles,
    int AddedFiles,
    int DeletedFiles,
    IReadOnlyList<string> AffectedDomains,
    int ViolationsCount,
    int DiagnosticsCount,
    int SignatureChangesCount);

public sealed record ChangeAnalysis(
    string Workspace,
    string BaseBranch,
    ChangeSummary Summary,
    IReadOnlyList<ChangedFileDetail> ChangedFiles,
    IReadOnlyList<SignatureChange> SignatureChanges,
    IReadOnlyList<ViolationResult> ViolationsInChanges,
    IReadOnlyList<DiagnosticResult> DiagnosticsInChanges,
    IReadOnlyList<TypeSummary> NewTypes);
