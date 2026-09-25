using CodeIntelligenceMcp.Roslyn;
using CodeIntelligenceMcp.Roslyn.Models;
using FluentAssertions;
using Xunit;

namespace CodeIntelligenceMcp.Tests.Integration;

// FixtureCoreOnly.slnf selects Fixture.Core; Fixture.Web (which calls into Core) is excluded.
[Collection("msbuild")]
public sealed class SolutionFilterIntegrationTests : IAsyncLifetime
{
    private RoslynWorkspaceIndex? _index;

    private RoslynWorkspaceIndex Index => _index!;

    public async Task InitializeAsync() =>
        _index = await RoslynLoader.LoadAsync(MsBuildFixture.ResolveFixtureFile("FixtureCoreOnly.slnf"), MsBuildFixture.CleanArch);

    public Task DisposeAsync()
    {
        _index?.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public void GetProjectDependencies_Slnf_ListsOnlySelectedProjects()
    {
        ProjectDependency deps = Index.GetProjectDependencies();

        deps.Projects.Select(p => p.Name).Should().Equal("Fixture.Core");
        deps.Dependencies.Should().BeEmpty();
    }

    [Fact]
    public async Task FindUsages_Slnf_ExcludesUsagesInUnselectedProjects()
    {
        IReadOnlyList<UsageResult> usages = await new ReferenceQueries(Index).FindUsagesAsync("GreetUseCase");

        usages.Should().NotContain(u => u.FilePath.StartsWith("Fixture.Web/"));
    }

    [Fact]
    public async Task FindCallers_Slnf_ExcludesCallersInUnselectedProjects()
    {
        IReadOnlyList<CallerResult> callers = await new ReferenceQueries(Index).FindCallersAsync("GreetUseCase", "Greet");

        callers.Should().BeEmpty();
    }
}
