namespace CodeIntelligenceMcp.Config;

internal static class McpConfigLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    internal static McpConfig Load(string configPath)
    {
        if (!File.Exists(configPath))
            throw new FileNotFoundException($"MCP config not found: {configPath}");

        string json = File.ReadAllText(configPath);
        McpConfig config = JsonSerializer.Deserialize<McpConfig>(json, JsonOptions)
            ?? throw new InvalidOperationException($"Failed to deserialize config: {configPath}");

        Validate(config);
        return config;
    }

    private static void Validate(McpConfig config)
    {
        foreach (WorkspaceConfig workspace in config.Workspaces)
        {
            switch (workspace.Type)
            {
                case "dotnet":
                    if (string.IsNullOrWhiteSpace(workspace.Solution))
                        throw new InvalidOperationException(
                            $"Workspace '{workspace.Name}': 'solution' is required for type 'dotnet'.");
                    if (!File.Exists(workspace.Solution))
                        throw new FileNotFoundException(
                            $"Workspace '{workspace.Name}': solution not found at '{workspace.Solution}'.");
                    break;

                case "asp-classic":
                    if (string.IsNullOrWhiteSpace(workspace.RootPath))
                        throw new InvalidOperationException(
                            $"Workspace '{workspace.Name}': 'rootPath' is required for type 'asp-classic'.");
                    if (!Directory.Exists(workspace.RootPath))
                        throw new DirectoryNotFoundException(
                            $"Workspace '{workspace.Name}': rootPath not found at '{workspace.RootPath}'.");
                    break;

                case "powershell":
                    if (string.IsNullOrWhiteSpace(workspace.RootPath))
                        throw new InvalidOperationException(
                            $"Workspace '{workspace.Name}': 'rootPath' is required for type 'powershell'.");
                    if (!Directory.Exists(workspace.RootPath))
                        throw new DirectoryNotFoundException(
                            $"Workspace '{workspace.Name}': rootPath not found at '{workspace.RootPath}'.");
                    break;

                case "python":
                    if (string.IsNullOrWhiteSpace(workspace.RootPath))
                        throw new InvalidOperationException(
                            $"Workspace '{workspace.Name}': 'rootPath' is required for type 'python'.");
                    if (!Directory.Exists(workspace.RootPath))
                        throw new DirectoryNotFoundException(
                            $"Workspace '{workspace.Name}': rootPath not found at '{workspace.RootPath}'.");
                    break;

                case "javascript":
                    if (string.IsNullOrWhiteSpace(workspace.RootPath))
                        throw new InvalidOperationException(
                            $"Workspace '{workspace.Name}': 'rootPath' is required for type 'javascript'.");
                    if (!Directory.Exists(workspace.RootPath))
                        throw new DirectoryNotFoundException(
                            $"Workspace '{workspace.Name}': rootPath not found at '{workspace.RootPath}'.");
                    break;

                default:
                    throw new InvalidOperationException(
                        $"Workspace '{workspace.Name}': unknown type '{workspace.Type}'. Expected 'dotnet', 'asp-classic', 'powershell', 'python', or 'javascript'.");
            }
        }
    }
}
