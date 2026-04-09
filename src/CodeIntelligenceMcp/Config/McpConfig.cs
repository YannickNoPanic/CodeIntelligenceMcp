namespace CodeIntelligenceMcp.Config;

internal sealed record McpConfig
{
    [JsonPropertyName("workspaces")]
    public List<WorkspaceConfig> Workspaces { get; init; } = [];
}

internal sealed record WorkspaceConfig
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("solution")]
    public string? Solution { get; init; }

    [JsonPropertyName("rootPath")]
    public string? RootPath { get; init; }

    [JsonPropertyName("cleanArchitecture")]
    public CleanArchitectureConfig? CleanArchitecture { get; init; }
}

internal sealed record CleanArchitectureConfig
{
    [JsonPropertyName("coreProject")]
    public string CoreProject { get; init; } = string.Empty;

    [JsonPropertyName("infraProject")]
    public string InfraProject { get; init; } = string.Empty;

    [JsonPropertyName("webProject")]
    public string WebProject { get; init; } = string.Empty;
}
