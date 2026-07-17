using System.Collections.Concurrent;
using CodeIntelligenceMcp.Config;

namespace CodeIntelligenceMcp.Workspaces;

internal sealed class WorkspaceCatalog(McpConfig config)
{
    private static readonly HashSet<string> KnownTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "dotnet",
        "asp-classic",
        "powershell",
        "python",
        "javascript"
    };

    private readonly ConcurrentDictionary<string, WorkspaceConfig> _runtime =
        new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<WorkspaceCatalogEntry> List()
    {
        IEnumerable<WorkspaceCatalogEntry> configured = config.Workspaces
            .Select(w => new WorkspaceCatalogEntry(w, "configured", GetPath(w)));
        IEnumerable<WorkspaceCatalogEntry> runtime = _runtime.Values
            .OrderBy(w => w.Name, StringComparer.OrdinalIgnoreCase)
            .Select(w => new WorkspaceCatalogEntry(w, "runtime", GetPath(w)));

        return [.. configured.Concat(runtime)];
    }

    public WorkspaceConfig? Resolve(
        string workspaceType,
        string workspace,
        Func<WorkspaceConfig, string?> getPath,
        Func<string, WorkspaceConfig> createAdHoc)
    {
        if (Path.IsPathRooted(workspace))
        {
            string normalizedPath = NormalizePath(workspace);
            WorkspaceConfig? configured = All()
                .FirstOrDefault(w =>
                    w.Type == workspaceType
                    && getPath(w) is string p
                    && string.Equals(NormalizePath(p), normalizedPath, StringComparison.OrdinalIgnoreCase));

            return configured ?? createAdHoc(normalizedPath);
        }

        return All()
            .FirstOrDefault(w =>
                w.Name == workspace
                && w.Type == workspaceType
                && getPath(w) is not null);
    }

    public RegistrationResult Register(
        string name,
        string type,
        string path,
        CleanArchitectureConfig? cleanArchitecture = null)
    {
        string normalizedName = name.Trim();
        string normalizedType = type.Trim();
        string normalizedPath = NormalizePath(path.Trim());

        string? validationError = Validate(normalizedName, normalizedType, normalizedPath);
        if (validationError is not null)
            return RegistrationResult.Fail(validationError);

        WorkspaceConfig workspace = normalizedType.Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            ? new WorkspaceConfig
            {
                Name = normalizedName,
                Type = "dotnet",
                Solution = normalizedPath,
                CleanArchitecture = cleanArchitecture
            }
            : new WorkspaceConfig
            {
                Name = normalizedName,
                Type = normalizedType,
                RootPath = normalizedPath
            };

        return _runtime.TryAdd(normalizedName, workspace)
            ? RegistrationResult.Ok(workspace)
            : RegistrationResult.Fail($"workspace '{normalizedName}' already exists");
    }

    public bool Unregister(string name) => _runtime.TryRemove(name.Trim(), out _);

    public WorkspaceConfig? GetRuntime(string name) =>
        _runtime.TryGetValue(name.Trim(), out WorkspaceConfig? workspace) ? workspace : null;

    public bool IsRuntime(string name) => _runtime.ContainsKey(name.Trim());

    public IReadOnlyList<string> KnownNames(string workspaceType) =>
        [.. All()
            .Where(w => w.Type == workspaceType)
            .Select(w => w.Name)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)];

    private string? Validate(string name, string type, string path)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "workspace name is required";

        if (name.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0)
            return "workspace name cannot contain path separators";

        if (!KnownTypes.Contains(type))
            return $"unknown type '{type}' - expected dotnet, asp-classic, powershell, python, or javascript";

        if (All().Any(w => string.Equals(w.Name, name, StringComparison.OrdinalIgnoreCase)))
            return $"workspace '{name}' already exists";

        if (type.Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            if (!File.Exists(path))
                return $"solution not found at '{path}'";

            string extension = Path.GetExtension(path);
            return extension.Equals(".sln", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".slnf", StringComparison.OrdinalIgnoreCase)
                ? null
                : "dotnet workspace path must be a .sln, .slnx, or .slnf file";
        }

        return Directory.Exists(path) ? null : $"rootPath not found at '{path}'";
    }

    private IEnumerable<WorkspaceConfig> All() => config.Workspaces.Concat(_runtime.Values);

    private static string? GetPath(WorkspaceConfig workspace) => workspace.Solution ?? workspace.RootPath;

    private static string NormalizePath(string path) => Path.GetFullPath(path).Replace('\\', '/');
}

internal sealed record WorkspaceCatalogEntry(WorkspaceConfig Workspace, string Source, string? Path);

internal sealed record RegistrationResult(bool Success, WorkspaceConfig? Workspace, string? Error)
{
    public static RegistrationResult Ok(WorkspaceConfig workspace) => new(true, workspace, null);
    public static RegistrationResult Fail(string error) => new(false, null, error);
}
