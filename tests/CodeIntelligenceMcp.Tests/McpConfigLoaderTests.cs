using CodeIntelligenceMcp.Config;
using FluentAssertions;
using Xunit;

namespace CodeIntelligenceMcp.Tests;

public sealed class McpConfigLoaderTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("citest-cfg").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    [Fact]
    public void Load_MissingFile_ReturnsEmptyConfigInsteadOfThrowing()
    {
        string path = Path.Combine(_dir, "does-not-exist.json");

        McpConfig config = McpConfigLoader.Load(path);

        config.Workspaces.Should().BeEmpty();
    }

    [Fact]
    public void Load_InvalidJson_ThrowsWithPathInMessage()
    {
        string path = Path.Combine(_dir, "bad.json");
        File.WriteAllText(path, "{ not json");

        Action act = () => McpConfigLoader.Load(path);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*invalid JSON*")
            .WithMessage($"*{path}*");
    }

    [Fact]
    public void Load_ValidConfigWithMissingWorkspacePath_DropsWorkspaceKeepsRest()
    {
        string path = Path.Combine(_dir, "cfg.json");
        string existingDir = Path.Combine(_dir, "scripts");
        Directory.CreateDirectory(existingDir);
        File.WriteAllText(path, $$"""
            {
              "workspaces": [
                { "name": "gone", "type": "dotnet", "solution": "C:/does/not/exist.sln" },
                { "name": "ok", "type": "powershell", "rootPath": {{System.Text.Json.JsonSerializer.Serialize(existingDir)}} }
              ]
            }
            """);

        McpConfig config = McpConfigLoader.Load(path);

        config.Workspaces.Should().ContainSingle(w => w.Name == "ok");
    }
}
