namespace CodeIntelligenceMcp.JavaScript.Models;

public sealed record JsParameterInfo(
    string Name,
    string? TypeAnnotation,
    string? DefaultValue);

public sealed record JsFunctionInfo(
    string Name,
    int LineStart,
    int LineEnd,
    IReadOnlyList<JsParameterInfo> Parameters,
    string? ReturnType,
    bool IsExported,
    bool IsAsync,
    bool IsGenerator,
    string Kind);   // "declaration", "arrow", "method"

public sealed record JsClassInfo(
    string Name,
    int LineStart,
    int LineEnd,
    string? Extends,
    IReadOnlyList<string> Implements,
    IReadOnlyList<JsFunctionInfo> Methods,
    bool IsExported,
    bool IsAbstract);

public sealed record JsImportInfo(
    string Source,
    IReadOnlyList<string> NamedImports,
    string? DefaultImport,
    string? NamespaceImport,
    bool IsTypeOnly,
    int Line);

public sealed record JsExportInfo(
    string Name,
    string Kind,       // "function", "class", "const", "default", "re-export"
    bool IsTypeOnly,
    int Line);

public sealed record JsInterfaceInfo(
    string Name,
    int LineStart,
    int LineEnd,
    IReadOnlyList<string> Extends,
    bool IsExported);

public sealed record JsTypeAliasInfo(
    string Name,
    int Line,
    bool IsExported);

public sealed record JsEnumInfo(
    string Name,
    int LineStart,
    int LineEnd,
    bool IsExported,
    bool IsConst);

public sealed record JsFileInfo(
    string FilePath,
    IReadOnlyList<JsFunctionInfo> Functions,
    IReadOnlyList<JsClassInfo> Classes,
    IReadOnlyList<JsImportInfo> Imports,
    IReadOnlyList<JsExportInfo> Exports,
    IReadOnlyList<JsInterfaceInfo> Interfaces,
    IReadOnlyList<JsTypeAliasInfo> TypeAliases,
    IReadOnlyList<JsEnumInfo> Enums,
    string ModuleType);   // "esm", "commonjs", "mixed", "none"

public sealed record JsPackageInfo(
    string Name,
    string? Version,
    bool IsDev);

public sealed record JsProjectInfo(
    string? ProjectName,
    string? Version,
    IReadOnlyList<JsPackageInfo> Dependencies,
    IReadOnlyList<string> Scripts,
    IReadOnlyList<string> DetectedFrameworks);
