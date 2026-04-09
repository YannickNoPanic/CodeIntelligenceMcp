namespace CodeIntelligenceMcp.Roslyn.Models;

public record ParameterDetail(string Name, string Type);

public record PropertyDetail(string Name, string Type, string Accessibility);

public record MethodSummary(
    string Name,
    string ReturnType,
    IReadOnlyList<ParameterDetail> Parameters,
    string Accessibility,
    int LineStart);

public record TypeInfo(
    string Name,
    string Namespace,
    string Kind,
    string FilePath,
    int LineStart,
    string? BaseType,
    IReadOnlyList<string> Interfaces,
    IReadOnlyList<string> Attributes,
    IReadOnlyList<PropertyDetail> Properties,
    IReadOnlyList<MethodSummary> Methods,
    IReadOnlyList<ParameterDetail> ConstructorParameters);

public record TypeSummary(string Name, string Namespace, string FilePath, int LineStart, string Kind);

public record ImplementationSummary(string TypeName, string Namespace, string FilePath, int LineStart);

public record UsageResult(string FilePath, int LineNumber, string Context, string UsageKind);

public record DependencyInfo(
    string TypeName,
    IReadOnlyList<ParameterDetail> ConstructorParameters,
    IReadOnlyList<ParameterDetail> Injects);

public record PublicSurfaceItem(string Name, string FilePath);

public record PublicSurface(
    string Namespace,
    IReadOnlyList<PublicSurfaceItem> Interfaces,
    IReadOnlyList<PublicSurfaceItem> PublicClasses,
    IReadOnlyList<PublicSurfaceItem> PublicRecords,
    IReadOnlyList<PublicSurfaceItem> Enums);
