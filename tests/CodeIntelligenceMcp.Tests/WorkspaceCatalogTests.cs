using CodeIntelligenceMcp.Config;
using CodeIntelligenceMcp.Workspaces;
using FluentAssertions;
using Xunit;

namespace CodeIntelligenceMcp.Tests;

public sealed class WorkspaceCatalogTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("citest-ws").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    [Fact]
    public void List_ConfiguredWorkspace_ReturnsConfiguredSource()
    {
        McpConfig config = ConfigWith(new WorkspaceConfig
        {
            Name = "configured",
            Type = "powershell",
            RootPath = _dir
        });
        var catalog = new WorkspaceCatalog(config);

        IReadOnlyList<WorkspaceCatalogEntry> entries = catalog.List();

        entries.Should().ContainSingle(e =>
            e.Workspace.Name == "configured"
            && e.Source == "configured"
            && e.Path == _dir);
    }

    [Fact]
    public void Register_ValidDotnetSolution_AddsRuntimeWorkspace()
    {
        string sln = Path.Combine(_dir, "App.slnx");
        File.WriteAllText(sln, string.Empty);
        var catalog = new WorkspaceCatalog(new McpConfig());

        RegistrationResult result = catalog.Register("current", "dotnet", sln);

        result.Success.Should().BeTrue();
        catalog.List().Should().ContainSingle(e =>
            e.Workspace.Name == "current"
            && e.Workspace.Type == "dotnet"
            && e.Workspace.Solution == sln.Replace('\\', '/')
            && e.Source == "runtime");
    }

    [Fact]
    public void Register_ValidFileWalkRoot_AddsRuntimeWorkspace()
    {
        var catalog = new WorkspaceCatalog(new McpConfig());

        RegistrationResult result = catalog.Register("scripts", "powershell", _dir);

        result.Success.Should().BeTrue();
        catalog.List().Should().ContainSingle(e =>
            e.Workspace.Name == "scripts"
            && e.Workspace.Type == "powershell"
            && e.Workspace.RootPath == _dir.Replace('\\', '/')
            && e.Source == "runtime");
    }

    [Fact]
    public void Register_DuplicateConfiguredName_Fails()
    {
        McpConfig config = ConfigWith(new WorkspaceConfig
        {
            Name = "current",
            Type = "powershell",
            RootPath = _dir
        });
        var catalog = new WorkspaceCatalog(config);

        RegistrationResult result = catalog.Register("current", "powershell", _dir);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("already exists");
    }

    [Fact]
    public void Register_NameWithPathSeparator_Fails()
    {
        var catalog = new WorkspaceCatalog(new McpConfig());

        RegistrationResult result = catalog.Register("bad/name", "powershell", _dir);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("path separators");
    }

    [Fact]
    public void Register_UnknownType_Fails()
    {
        var catalog = new WorkspaceCatalog(new McpConfig());

        RegistrationResult result = catalog.Register("current", "ruby", _dir);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("unknown type");
    }

    [Fact]
    public void Register_MissingDotnetPath_Fails()
    {
        var catalog = new WorkspaceCatalog(new McpConfig());

        RegistrationResult result = catalog.Register("current", "dotnet", Path.Combine(_dir, "missing.sln"));

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public void Resolve_RuntimeName_ReturnsRuntimeWorkspace()
    {
        var catalog = new WorkspaceCatalog(new McpConfig());
        catalog.Register("scripts", "powershell", _dir);

        WorkspaceConfig? workspace = catalog.Resolve(
            "powershell",
            "scripts",
            ws => ws.RootPath,
            path => new WorkspaceConfig { Name = path, Type = "powershell", RootPath = path });

        workspace.Should().NotBeNull();
        workspace!.Name.Should().Be("scripts");
    }

    [Fact]
    public void Resolve_AbsolutePathMatchingRuntime_ReusesRuntimeName()
    {
        var catalog = new WorkspaceCatalog(new McpConfig());
        catalog.Register("scripts", "powershell", _dir);

        WorkspaceConfig? workspace = catalog.Resolve(
            "powershell",
            _dir,
            ws => ws.RootPath,
            path => new WorkspaceConfig { Name = path, Type = "powershell", RootPath = path });

        workspace.Should().NotBeNull();
        workspace!.Name.Should().Be("scripts");
    }

    [Fact]
    public void Unregister_RuntimeWorkspace_RemovesIt()
    {
        var catalog = new WorkspaceCatalog(new McpConfig());
        catalog.Register("scripts", "powershell", _dir);

        bool removed = catalog.Unregister("scripts");

        removed.Should().BeTrue();
        catalog.List().Should().NotContain(e => e.Workspace.Name == "scripts");
    }

    private static McpConfig ConfigWith(params WorkspaceConfig[] workspaces) => new()
    {
        Workspaces = [.. workspaces]
    };
}
