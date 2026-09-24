using CodeIntelligenceMcp.Roslyn;
using CodeIntelligenceMcp.Roslyn.Models;
using FluentAssertions;
using Xunit;

namespace CodeIntelligenceMcp.Tests;

// Covers the symbol-based rules that work on an in-memory index. Document-based rules
// (core-no-http, dto-in-core, empty-catch, throw-ex, async-over-sync) need a real
// Solution and are covered in Integration/RoslynIntegrationTests.
public sealed class ViolationDetectorTests
{
    private static ViolationDetector Detector(RoslynWorkspaceIndex index) => new(index, TestIndex.CleanArch);

    [Fact]
    public async Task DetectAsync_MissingCancellationToken_FlagsPublicAsyncWithoutToken()
    {
        RoslynWorkspaceIndex index = TestIndex.Create(("App.Core",
            """
            using System.Threading;
            using System.Threading.Tasks;
            namespace App.Core;
            public class Svc
            {
                public async Task RunAsync() { await Task.Delay(1); }
                public async Task OkAsync(CancellationToken ct) { await Task.Delay(1, ct); }
            }
            """));

        IReadOnlyList<ViolationResult> violations = await Detector(index).DetectAsync("missing-cancellation-token", CancellationToken.None);

        violations.Should().ContainSingle().Which.MethodName.Should().Be("RunAsync");
    }

    [Fact]
    public async Task DetectAsync_MissingCancellationToken_SkipsTestProjects()
    {
        RoslynWorkspaceIndex index = TestIndex.Create(("App.Tests",
            """
            using System.Threading.Tasks;
            namespace App.Tests;
            public class SvcTests
            {
                public async Task RunAsync() { await Task.Delay(1); }
            }
            """));

        IReadOnlyList<ViolationResult> violations = await Detector(index).DetectAsync("missing-cancellation-token", CancellationToken.None);

        violations.Should().BeEmpty();
    }

    [Fact]
    public async Task DetectAsync_NoAsyncVoid_FlagsAsyncVoidButNotEventHandlers()
    {
        RoslynWorkspaceIndex index = TestIndex.Create(("App.Web",
            """
            using System;
            using System.Threading.Tasks;
            namespace App.Web;
            public class Component
            {
                public async void Fire() { await Task.Delay(1); }
                public async void OnClick(EventArgs e) { await Task.Delay(1); }
            }
            """));

        IReadOnlyList<ViolationResult> violations = await Detector(index).DetectAsync("no-async-void", CancellationToken.None);

        violations.Should().ContainSingle().Which.MethodName.Should().Be("Fire");
    }

    [Fact]
    public async Task DetectAsync_TooManyParams_FlagsMethodWithSixParameters()
    {
        RoslynWorkspaceIndex index = TestIndex.Create(("App.Core",
            """
            namespace App.Core;
            public class Svc
            {
                public void Wide(int a, int b, int c, int d, int e, int f) { }
                public void Narrow(int a, int b) { }
            }
            """));

        IReadOnlyList<ViolationResult> violations = await Detector(index).DetectAsync("too-many-params", CancellationToken.None);

        violations.Should().ContainSingle().Which.MethodName.Should().Be("Wide");
    }

    [Fact]
    public async Task DetectAsync_UsecaseNotSealed_FlagsUnsealedUseCaseImplementation()
    {
        RoslynWorkspaceIndex index = TestIndex.Create(("App.Core",
            """
            namespace App.Core;
            public interface IUseCase<TIn> { }
            public class CreateOrderUseCase : IUseCase<int> { }
            public sealed class DeleteOrderUseCase : IUseCase<int> { }
            """));

        IReadOnlyList<ViolationResult> violations = await Detector(index).DetectAsync("usecase-not-sealed", CancellationToken.None);

        violations.Should().ContainSingle().Which.TypeName.Should().Be("CreateOrderUseCase");
    }

    private const string OrderRepositorySource =
        """
        namespace App.Infrastructure;
        public class OrderRepository
        {
            public void Add(int order) { }
            public void GetById(int id) { }
            public void ListAsync() { }
            public void UpdateAsync(int order) { }
            public void Delete(int id) { }
            public void SaveChangesAsync() { }
            public void AddIfNotExisting(int order) { }
            public void InsertIfMissingAsync(int order) { }
            public void TryAddAsync(int order) { }
            public void CanCreate(int id) { }
            public void DisableAsync(int id) { }
            public void Remove(int id) { }
            public void GetAsyncSnapshot() { }
            public void Trying() { }
        }
        """;

    [Fact]
    public async Task DetectAsync_RepositoryBusinessLogic_FlagsConditionalAndDecisionMethodsOnly()
    {
        RoslynWorkspaceIndex index = TestIndex.Create(("App.Infrastructure", OrderRepositorySource));

        IReadOnlyList<ViolationResult> violations = await Detector(index).DetectAsync("repository-business-logic", CancellationToken.None);

        violations.Select(v => v.MethodName).Should().BeEquivalentTo(
            "AddIfNotExisting", "InsertIfMissingAsync", "TryAddAsync", "CanCreate");
    }

    [Fact]
    public async Task DetectAsync_RepositoryNaming_FlagsNonStandardVerbsButNotBusinessLogicHits()
    {
        RoslynWorkspaceIndex index = TestIndex.Create(("App.Infrastructure", OrderRepositorySource));

        IReadOnlyList<ViolationResult> violations = await Detector(index).DetectAsync("repository-naming", CancellationToken.None);

        violations.Select(v => v.MethodName).Should().BeEquivalentTo("DisableAsync", "Remove", "Trying");
    }

    [Fact]
    public async Task DetectAsync_RepositoryRules_SkipTestProjects()
    {
        RoslynWorkspaceIndex index = TestIndex.Create(("App.Infrastructure.Tests",
            """
            namespace App.Infrastructure.Tests;
            public class FakeOrderRepository
            {
                public void AddIfNotExisting(int order) { }
                public void DisableAsync(int id) { }
            }
            """));

        ViolationDetector detector = Detector(index);

        (await detector.DetectAsync("repository-business-logic", CancellationToken.None)).Should().BeEmpty();
        (await detector.DetectAsync("repository-naming", CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task DetectAsync_UnknownRule_ThrowsArgumentException()
    {
        RoslynWorkspaceIndex index = TestIndex.Create(("App.Core", "namespace App.Core; public class A { }"));

        Func<Task> act = () => Detector(index).DetectAsync("not-a-rule", CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*not-a-rule*");
    }
}
