using CodeIntelligenceMcp.Roslyn;
using Xunit;

namespace CodeIntelligenceMcp.Tests.Integration;

[CollectionDefinition("msbuild")]
public sealed class MsBuildCollection : ICollectionFixture<MsBuildFixture>;

// Loads the fixture solution through the real MSBuildWorkspace path exactly once and
// shares the index across all integration tests. MSBuildLocator registration reuses
// RoslynLoader's idempotent guard.
public sealed class MsBuildFixture : IAsyncLifetime
{
    private RoslynWorkspaceIndex? _index;

    public RoslynWorkspaceIndex Index => _index
        ?? throw new InvalidOperationException("Fixture index was not initialized");

    public static readonly CleanArchitectureNames CleanArch =
        new("Fixture.Core", "Fixture.Infrastructure", "Fixture.Web");

    public async Task InitializeAsync()
    {
        string solutionPath = ResolveFixtureSolution();
        _index = await RoslynLoader.LoadAsync(solutionPath, CleanArch);
    }

    public Task DisposeAsync()
    {
        _index?.Dispose();
        return Task.CompletedTask;
    }

    // CallerFilePath resolves against the source location at compile time, so the fixture
    // is found even when tests run from an isolated output directory outside the repo.
    private static string ResolveFixtureSolution([System.Runtime.CompilerServices.CallerFilePath] string thisFile = "")
    {
        string testProjectDir = Path.GetDirectoryName(Path.GetDirectoryName(thisFile))
            ?? throw new InvalidOperationException("Could not resolve the test project directory");

        string candidate = Path.GetFullPath(Path.Combine(
            testProjectDir, "..", "fixtures", "FixtureSolution", "FixtureSolution.sln"));

        return File.Exists(candidate)
            ? candidate
            : throw new FileNotFoundException($"Fixture solution not found at '{candidate}'");
    }
}
