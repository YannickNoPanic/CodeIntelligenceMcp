using CodeIntelligenceMcp.Config;
using CodeIntelligenceMcp.Roslyn;
using CodeIntelligenceMcp.Workspaces;
using FluentAssertions;
using Xunit;

namespace CodeIntelligenceMcp.Tests;

public sealed class WorkspaceAccessTests
{
    private sealed class ThrowingProvider(Exception ex) : IWorkspaceProvider<object>
    {
        public Task<object?> GetAsync(string workspace, CancellationToken ct = default) => Task.FromException<object?>(ex);
        public bool Invalidate(string workspace) => false;
        public bool IsLoaded(string workspace) => false;
    }

    private sealed class NullProvider : IWorkspaceProvider<object>
    {
        public Task<object?> GetAsync(string workspace, CancellationToken ct = default) => Task.FromResult<object?>(null);
        public bool Invalidate(string workspace) => false;
        public bool IsLoaded(string workspace) => false;
    }

    private static McpConfig ConfigWith(params (string Name, string Type)[] workspaces) => new()
    {
        Workspaces = [.. workspaces.Select(w => new WorkspaceConfig { Name = w.Name, Type = w.Type })]
    };

    [Fact]
    public async Task GetAsync_UnknownWorkspace_ErrorListsKnownNames()
    {
        McpConfig config = ConfigWith(("alpha", "dotnet"), ("beta", "dotnet"), ("legacy", "asp-classic"));

        (object? index, string? error) = await WorkspaceAccess.GetAsync(new NullProvider(), config, "dotnet", "gamma", CancellationToken.None);

        index.Should().BeNull();
        error.Should().Contain("gamma").And.Contain("alpha").And.Contain("beta").And.NotContain("legacy");
    }

    [Fact]
    public async Task GetAsync_WorkspaceLoadException_SurfacesHintAndDetail()
    {
        var ex = new WorkspaceLoadException("solution failed to load", "install the .NET SDK", ["diag1"]);

        (object? index, string? error) = await WorkspaceAccess.GetAsync(new ThrowingProvider(ex), ConfigWith(), "dotnet", "C:/x/y.sln", CancellationToken.None);

        index.Should().BeNull();
        error.Should().Contain("solution failed to load").And.Contain("install the .NET SDK").And.Contain("diag1");
    }

    [Fact]
    public async Task GetAsync_UnexpectedException_ShortMessageNoStackTrace()
    {
        var ex = new InvalidOperationException("boom");

        (object? index, string? error) = await WorkspaceAccess.GetAsync(new ThrowingProvider(ex), ConfigWith(), "dotnet", "ws", CancellationToken.None);

        index.Should().BeNull();
        error.Should().Contain("boom").And.NotContain("   at ");
    }

    [Fact]
    public async Task GetAsync_Cancellation_Rethrows()
    {
        var ex = new OperationCanceledException();

        Func<Task> act = () => WorkspaceAccess.GetAsync(new ThrowingProvider(ex), ConfigWith(), "dotnet", "ws", CancellationToken.None);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
