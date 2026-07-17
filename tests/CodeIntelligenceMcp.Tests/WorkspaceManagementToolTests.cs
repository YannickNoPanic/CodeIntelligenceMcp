using System.Text.Json;
using CodeIntelligenceMcp.AspClassic;
using CodeIntelligenceMcp.Config;
using CodeIntelligenceMcp.JavaScript;
using CodeIntelligenceMcp.PowerShell;
using CodeIntelligenceMcp.Python;
using CodeIntelligenceMcp.Roslyn;
using CodeIntelligenceMcp.Tools;
using CodeIntelligenceMcp.Workspaces;
using FluentAssertions;
using Xunit;

namespace CodeIntelligenceMcp.Tests;

public sealed class WorkspaceManagementToolTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("citest-tool").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    [Fact]
    public void ListWorkspaces_ConfiguredAndRuntime_IncludesSource()
    {
        McpConfig config = new()
        {
            Workspaces =
            [
                new WorkspaceConfig { Name = "configured", Type = "powershell", RootPath = _dir }
            ]
        };
        var catalog = new WorkspaceCatalog(config);
        catalog.Register("runtime", "powershell", _dir);
        WorkspaceManagementTool tool = CreateTool(catalog);

        string json = tool.ListWorkspaces();

        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement workspaces = doc.RootElement.GetProperty("workspaces");
        workspaces.EnumerateArray().Should().Contain(e =>
            e.GetProperty("name").GetString() == "configured"
            && e.GetProperty("source").GetString() == "configured");
        workspaces.EnumerateArray().Should().Contain(e =>
            e.GetProperty("name").GetString() == "runtime"
            && e.GetProperty("source").GetString() == "runtime");
    }

    [Fact]
    public void RegisterWorkspace_ValidDotnetSolution_ReturnsRegistered()
    {
        string sln = Path.Combine(_dir, "App.slnx");
        File.WriteAllText(sln, string.Empty);
        var catalog = new WorkspaceCatalog(new McpConfig());
        WorkspaceManagementTool tool = CreateTool(catalog);

        string json = tool.RegisterWorkspace("current", "dotnet", sln);

        json.Should().Contain("\"registered\":true");
        catalog.GetRuntime("current").Should().NotBeNull();
    }

    [Fact]
    public void RegisterWorkspace_InvalidPath_ReturnsError()
    {
        var catalog = new WorkspaceCatalog(new McpConfig());
        WorkspaceManagementTool tool = CreateTool(catalog);

        string json = tool.RegisterWorkspace("current", "dotnet", Path.Combine(_dir, "missing.sln"));

        json.Should().Contain("\"error\"");
        json.Should().Contain("not found");
    }

    [Fact]
    public void UnregisterWorkspace_RuntimeWorkspace_InvalidatesProviders()
    {
        var catalog = new WorkspaceCatalog(new McpConfig());
        catalog.Register("current", "powershell", _dir);
        var ps = new FakeProvider<PowerShellIndex>();
        WorkspaceManagementTool tool = CreateTool(catalog, ps: ps);

        string json = tool.UnregisterWorkspace("current");

        json.Should().Contain("\"unregistered\":true");
        ps.Invalidated.Should().Contain("current");
        catalog.GetRuntime("current").Should().BeNull();
    }

    [Fact]
    public void UnregisterWorkspace_ConfiguredWorkspace_ReturnsError()
    {
        McpConfig config = new()
        {
            Workspaces =
            [
                new WorkspaceConfig { Name = "configured", Type = "powershell", RootPath = _dir }
            ]
        };
        var catalog = new WorkspaceCatalog(config);
        WorkspaceManagementTool tool = CreateTool(catalog);

        string json = tool.UnregisterWorkspace("configured");

        json.Should().Contain("\"error\"");
        json.Should().Contain("runtime");
    }

    [Fact]
    public void SaveWorkspace_RuntimeWorkspace_WritesConfigFile()
    {
        string configPath = Path.Combine(_dir, "mcp-config.json");
        File.WriteAllText(configPath, """
            {
              "workspaces": []
            }
            """);
        string sln = Path.Combine(_dir, "App.slnx");
        File.WriteAllText(sln, string.Empty);
        var catalog = new WorkspaceCatalog(new McpConfig());
        catalog.Register("current", "dotnet", sln);
        WorkspaceManagementTool tool = CreateTool(catalog, configSource: new McpConfigSource(configPath));

        string json = tool.SaveWorkspace("current");

        json.Should().Contain("\"saved\":true");
        McpConfig saved = McpConfigLoader.Load(configPath);
        saved.Workspaces.Should().ContainSingle(w => w.Name == "current" && w.Solution == sln.Replace('\\', '/'));
    }

    [Fact]
    public void SaveWorkspace_ConfiguredWorkspace_ReturnsError()
    {
        string configPath = Path.Combine(_dir, "mcp-config.json");
        File.WriteAllText(configPath, """
            {
              "workspaces": []
            }
            """);
        McpConfig config = new()
        {
            Workspaces =
            [
                new WorkspaceConfig { Name = "configured", Type = "powershell", RootPath = _dir }
            ]
        };
        var catalog = new WorkspaceCatalog(config);
        WorkspaceManagementTool tool = CreateTool(catalog, configSource: new McpConfigSource(configPath));

        string json = tool.SaveWorkspace("configured");

        json.Should().Contain("\"error\"");
        json.Should().Contain("runtime");
    }

    private static WorkspaceManagementTool CreateTool(
        WorkspaceCatalog catalog,
        FakeProvider<RoslynWorkspaceIndex>? roslyn = null,
        FakeProvider<AspIndex>? asp = null,
        FakeProvider<PowerShellIndex>? ps = null,
        FakeProvider<PythonIndex>? py = null,
        FakeProvider<JsIndex>? js = null,
        McpConfigSource? configSource = null)
    {
        return new WorkspaceManagementTool(
            roslyn ?? new FakeProvider<RoslynWorkspaceIndex>(),
            asp ?? new FakeProvider<AspIndex>(),
            ps ?? new FakeProvider<PowerShellIndex>(),
            py ?? new FakeProvider<PythonIndex>(),
            js ?? new FakeProvider<JsIndex>(),
            catalog,
            configSource ?? new McpConfigSource(Path.Combine(Path.GetTempPath(), "unused-mcp-config.json")));
    }

    private sealed class FakeProvider<TIndex> : IWorkspaceProvider<TIndex>
        where TIndex : class
    {
        public List<string> Invalidated { get; } = [];

        public Task<TIndex?> GetAsync(string workspace, CancellationToken ct = default) => Task.FromResult<TIndex?>(null);
        public bool IsLoaded(string workspace) => false;

        public bool Invalidate(string workspace)
        {
            Invalidated.Add(workspace);
            return true;
        }
    }
}
