namespace CodeIntelligenceMcp.Config;

internal static class McpConfigLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    // A broken individual workspace must not take down the server for the healthy ones:
    // invalid entries are logged and dropped, only a missing/unreadable config file is fatal.
    internal static McpConfig Load(string configPath, ILogger? logger = null)
    {
        if (!File.Exists(configPath))
            throw new FileNotFoundException($"MCP config not found: {configPath}");

        string json = File.ReadAllText(configPath);
        McpConfig config = JsonSerializer.Deserialize<McpConfig>(json, JsonOptions)
            ?? throw new InvalidOperationException($"Failed to deserialize config: {configPath}");

        List<WorkspaceConfig> valid = [];
        foreach (WorkspaceConfig workspace in config.Workspaces)
        {
            string? error = Validate(workspace);
            if (error is null)
            {
                valid.Add(workspace);
            }
            else
            {
                logger?.LogWarning("Skipping workspace '{Workspace}': {Reason}", workspace.Name, error);
            }
        }

        return new McpConfig { Workspaces = valid };
    }

    private static string? Validate(WorkspaceConfig workspace) => workspace.Type switch
    {
        "dotnet" when string.IsNullOrWhiteSpace(workspace.Solution) =>
            "'solution' is required for type 'dotnet'",
        "dotnet" when !File.Exists(workspace.Solution) =>
            $"solution not found at '{workspace.Solution}'",
        "dotnet" => null,

        "asp-classic" or "powershell" or "python" or "javascript" when string.IsNullOrWhiteSpace(workspace.RootPath) =>
            $"'rootPath' is required for type '{workspace.Type}'",
        "asp-classic" or "powershell" or "python" or "javascript" when !Directory.Exists(workspace.RootPath) =>
            $"rootPath not found at '{workspace.RootPath}'",
        "asp-classic" or "powershell" or "python" or "javascript" => null,

        _ => $"unknown type '{workspace.Type}' — expected 'dotnet', 'asp-classic', 'powershell', 'python', or 'javascript'"
    };
}
