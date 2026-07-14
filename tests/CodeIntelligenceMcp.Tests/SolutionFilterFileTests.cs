using CodeIntelligenceMcp.Roslyn;
using FluentAssertions;
using Xunit;

namespace CodeIntelligenceMcp.Tests;

public sealed class SolutionFilterFileTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("citest-slnf").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    [Fact]
    public void Parse_RelativePaths_ResolvedAgainstSlnfDirectory()
    {
        string slnf = Path.Combine(_dir, "part.slnf");
        File.WriteAllText(slnf, """{ "solution": { "path": "sub\\Full.sln", "projects": [ "sub\\src\\A\\A.csproj" ] } }""");

        SolutionFilterFile parsed = SolutionFilterFile.Parse(slnf);

        parsed.SolutionPath.Should().Be(Path.Combine(_dir, "sub", "Full.sln"));
        parsed.ProjectPaths.Should().ContainSingle().Which.Should().EndWith("A/A.csproj");
    }

    [Fact]
    public void Parse_MissingSolutionSection_ThrowsWorkspaceLoadException()
    {
        string slnf = Path.Combine(_dir, "broken.slnf");
        File.WriteAllText(slnf, """{ "foo": 1 }""");

        Action act = () => SolutionFilterFile.Parse(slnf);

        act.Should().Throw<WorkspaceLoadException>();
    }

    [Fact]
    public void Parse_ProjectPathsAreCaseInsensitive()
    {
        string slnf = Path.Combine(_dir, "part.slnf");
        File.WriteAllText(slnf, """{ "solution": { "path": "Full.sln", "projects": [ "SRC\\A\\A.csproj" ] } }""");

        SolutionFilterFile parsed = SolutionFilterFile.Parse(slnf);

        string probe = Path.Combine(_dir, "src", "a", "a.csproj").Replace('\\', '/');
        parsed.ProjectPaths.Contains(probe).Should().BeTrue();
    }
}
