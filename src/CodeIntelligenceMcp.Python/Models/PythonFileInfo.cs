namespace CodeIntelligenceMcp.Python.Models;

public sealed record PythonParameterInfo(
    string Name,
    string? TypeHint,
    string? DefaultValue);

public sealed record PythonFunctionInfo(
    string Name,
    int LineStart,
    int LineEnd,
    IReadOnlyList<PythonParameterInfo> Parameters,
    string? ReturnTypeHint,
    IReadOnlyList<string> Decorators,
    bool IsAsync,
    bool IsMethod);

public sealed record PythonClassInfo(
    string Name,
    int LineStart,
    int LineEnd,
    IReadOnlyList<string> BaseClasses,
    IReadOnlyList<PythonFunctionInfo> Methods,
    IReadOnlyList<string> Decorators);

public sealed record PythonImportInfo(
    string Module,
    IReadOnlyList<string> Names,
    bool IsRelative,
    int Line);

public sealed record PythonFileInfo(
    string FilePath,
    IReadOnlyList<PythonFunctionInfo> Functions,
    IReadOnlyList<PythonClassInfo> Classes,
    IReadOnlyList<PythonImportInfo> Imports,
    IReadOnlyList<string> ExportedNames,
    IReadOnlyList<string> DetectedFrameworks);

public sealed record PythonPackageInfo(
    string Name,
    string? VersionConstraint,
    string Source);

public sealed record PythonProjectInfo(
    string? ProjectName,
    string? PythonVersion,
    IReadOnlyList<PythonPackageInfo> Dependencies,
    IReadOnlyList<PythonPackageInfo> DevDependencies);
