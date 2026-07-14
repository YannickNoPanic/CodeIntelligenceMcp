using CodeIntelligenceMcp.Roslyn;
using CodeIntelligenceMcp.Roslyn.Models;
using FluentAssertions;
using Xunit;

namespace CodeIntelligenceMcp.Tests.Integration;

[Collection("msbuild")]
public sealed class RoslynIntegrationTests(MsBuildFixture fixture)
{
    [Fact]
    public void LoadAsync_FixtureSolution_IndexesAllTypes()
    {
        RoslynWorkspaceIndex index = fixture.Index;

        index.TypeCount.Should().BeGreaterThanOrEqualTo(5);
        index.GetType("GreetUseCase").Should().NotBeNull();
    }

    [Fact]
    public void FindImplementations_FixtureInterface_FindsConcreteType()
    {
        IReadOnlyList<ImplementationSummary> impls = fixture.Index.FindImplementations("IGreetUseCase");

        impls.Should().ContainSingle(i => i.TypeName == "GreetUseCase");
    }

    [Fact]
    public void GetType_FixtureType_ReturnsWorkspaceRelativePath()
    {
        CodeIntelligenceMcp.Roslyn.Models.TypeInfo? type = fixture.Index.GetType("GreetUseCase");

        type.Should().NotBeNull();
        type!.FilePath.Should().Be("Fixture.Core/GreetUseCase.cs");
    }

    [Fact]
    public async Task FindCallers_GreetMethod_FindsWebCaller()
    {
        IReadOnlyList<CallerResult> callers =
            await new ReferenceQueries(fixture.Index).FindCallersAsync("GreetUseCase", "Greet");

        callers.Should().Contain(c => c.CallerType == "Caller" && c.Depth == 1);
    }

    [Fact]
    public async Task FindCallers_DepthTwo_FindsCallersOfCallers()
    {
        // Chain: Outer.Run -> Caller.InvokeConcrete -> GreetUseCase.Greet.
        IReadOnlyList<CallerResult> callers =
            await new ReferenceQueries(fixture.Index).FindCallersAsync("GreetUseCase", "Greet", depth: 2);

        callers.Should().Contain(c => c.CallerType == "Caller" && c.CallerMethod == "InvokeConcrete" && c.Depth == 1);
        callers.Should().Contain(c => c.CallerType == "Outer" && c.CallerMethod == "Run" && c.Depth == 2);
    }

    [Fact]
    public async Task DetectAsync_CoreNoHttp_FlagsHttpViolation()
    {
        var detector = new ViolationDetector(fixture.Index, MsBuildFixture.CleanArch);

        IReadOnlyList<ViolationResult> violations = await detector.DetectAsync("core-no-http", CancellationToken.None);

        violations.Should().Contain(v => v.FilePath.EndsWith("HttpViolation.cs"));
    }

    [Fact]
    public async Task FindUsages_GreetUseCase_FindsInstantiationAndInjection()
    {
        IReadOnlyList<UsageResult> usages =
            await new ReferenceQueries(fixture.Index).FindUsagesAsync("GreetUseCase", CancellationToken.None);

        usages.Should().NotBeEmpty();
        usages.Should().OnlyContain(u => !Path.IsPathRooted(u.FilePath));
    }

    [Fact]
    public void IsStaleCached_NonGitFixture_IsAlwaysFalse()
    {
        fixture.Index.IsStaleCached().Should().BeFalse();
    }

    [Fact]
    public void GetProjectDependencies_Fixture_HasExpectedEdges()
    {
        ProjectDependency deps = fixture.Index.GetProjectDependencies();

        deps.Projects.Select(p => p.Name).Should().BeEquivalentTo(
            ["Fixture.Core", "Fixture.Infrastructure", "Fixture.Web"]);
        deps.Dependencies.Should().Contain(e => e.From == "Fixture.Web" && e.To == "Fixture.Core");
    }
}
