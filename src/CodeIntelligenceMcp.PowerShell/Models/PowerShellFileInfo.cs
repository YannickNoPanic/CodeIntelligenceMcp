namespace CodeIntelligenceMcp.PowerShell.Models;

public sealed record PowerShellParameterInfo(
    string Name,
    string? Type,
    bool IsMandatory,
    bool IsFromPipeline,
    string? DefaultValue);

public sealed record PowerShellFunctionInfo(
    string Name,
    IReadOnlyList<PowerShellParameterInfo> Parameters,
    int LineStart,
    int LineEnd,
    bool HasCmdletBinding,
    bool SupportsPipeline,
    bool HasTryCatch);

public sealed record ImportedModule(
    string Name,
    string? Version,
    int Line);

public sealed record ScriptVariable(
    string Name,
    int Line);

public sealed record CmdletUsage(
    string Name,
    int Count);

public sealed record PowerShellFileInfo(
    string FilePath,
    IReadOnlyList<PowerShellFunctionInfo> Functions,
    IReadOnlyList<ImportedModule> ImportedModules,
    IReadOnlyList<ScriptVariable> Variables,
    IReadOnlyList<CmdletUsage> CmdletUsages);

public sealed record PowerShellModuleManifest(
    string Name,
    string? Version,
    string ManifestPath,
    IReadOnlyList<string> RequiredModules,
    IReadOnlyList<string> ExportedFunctions,
    IReadOnlyList<string> ExportedCmdlets,
    string? Description);
