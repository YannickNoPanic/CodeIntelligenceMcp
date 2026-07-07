namespace CodeIntelligenceMcp.Roslyn;

// Carries a user-actionable hint and optional diagnostics across the tool boundary so the
// MCP client gets a short, useful error instead of the SDK's generic failure message.
public sealed class WorkspaceLoadException(string message, string? hint = null, IReadOnlyList<string>? detail = null)
    : Exception(message)
{
    public string? Hint { get; } = hint;
    public IReadOnlyList<string>? Detail { get; } = detail;
}
