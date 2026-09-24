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
    public async Task DetectAsync_EmptyCatch_FlagsSwallowedException()
    {
        var detector = new ViolationDetector(fixture.Index, MsBuildFixture.CleanArch);

        IReadOnlyList<ViolationResult> violations = await detector.DetectAsync("empty-catch", CancellationToken.None);

        violations.Should().Contain(v => v.FilePath.EndsWith("BadPractices.cs"));
    }

    [Fact]
    public async Task DetectAsync_CommentTooLong_FlagsMultiLineBlocksAndSummariesOnly()
    {
        var detector = new ViolationDetector(fixture.Index, MsBuildFixture.CleanArch);

        IReadOnlyList<ViolationResult> violations = await detector.DetectAsync("comment-too-long", CancellationToken.None);

        violations.Where(v => v.FilePath.EndsWith("CommentBait.cs"))
            .Select(v => v.LineNumber)
            .Should().BeEquivalentTo([11, 15]);
    }

    [Fact]
    public async Task DetectAsync_ThrowEx_FlagsStackTraceReset()
    {
        var detector = new ViolationDetector(fixture.Index, MsBuildFixture.CleanArch);

        IReadOnlyList<ViolationResult> violations = await detector.DetectAsync("throw-ex", CancellationToken.None);

        violations.Should().Contain(v => v.FilePath.EndsWith("BadPractices.cs"));
    }

    [Fact]
    public async Task DetectAsync_DtoInCore_FlagsOrderDto()
    {
        var detector = new ViolationDetector(fixture.Index, MsBuildFixture.CleanArch);

        IReadOnlyList<ViolationResult> violations = await detector.DetectAsync("dto-in-core", CancellationToken.None);

        violations.Should().Contain(v => v.FilePath.EndsWith("OrderDto.cs"));
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
    public async Task AnalyzeAsync_ComplexMethod_ScoresFiveAndSimpleScoresOne()
    {
        var analyzer = new ComplexityAnalyzer(fixture.Index);

        IReadOnlyList<MethodComplexity> all = await analyzer.AnalyzeAsync(minComplexity: 1, ct: CancellationToken.None);

        all.Should().Contain(m => m.MethodName == "Score" && m.Complexity == 5);
        all.Should().Contain(m => m.MethodName == "Simple" && m.Complexity == 1);
    }

    [Fact]
    public async Task AnalyzeAsync_MinComplexityThreshold_FiltersSimpleMethods()
    {
        var analyzer = new ComplexityAnalyzer(fixture.Index);

        IReadOnlyList<MethodComplexity> filtered = await analyzer.AnalyzeAsync(minComplexity: 4, ct: CancellationToken.None);

        filtered.Should().Contain(m => m.MethodName == "Score");
        filtered.Should().NotContain(m => m.MethodName == "Simple");
    }

    [Fact]
    public async Task GetChangeRiskAsync_UntestedReferencedType_ReturnsScoreAndReferences()
    {
        var analyzer = new RiskAnalyzer(fixture.Index);

        ChangeRiskResult? risk = await analyzer.GetChangeRiskAsync("GreetUseCase", CancellationToken.None);

        risk.Should().NotBeNull();
        risk!.HasTests.Should().BeFalse();
        risk.ReferencedBy.Should().Contain("Caller");
        risk.RiskScore.Should().BeGreaterThan(0);
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
