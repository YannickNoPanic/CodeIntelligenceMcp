using CodeIntelligenceMcp.Roslyn;
using CodeIntelligenceMcp.Roslyn.Models;
using FluentAssertions;
using Xunit;

namespace CodeIntelligenceMcp.Tests;

public sealed class TestCoverageTests
{
    [Fact]
    public void GetTestCoverage_RecordNamedUseCase_IsNotCountedAsUseCase()
    {
        RoslynWorkspaceIndex index = TestIndex.Create(("App.Core",
            """
            namespace App.Core;
            public sealed record CoveredUseCase(string Name);
            public sealed class PlaceOrderUseCase { }
            """));

        TestCoverageResult coverage = index.GetTestCoverage();

        coverage.TotalUseCases.Should().Be(1);
    }

    [Fact]
    public void GetTestCoverage_SameNameInTwoNamespaces_CoversOnlyTheMatchingNamespace()
    {
        RoslynWorkspaceIndex index = TestIndex.Create(
            ("App.Core",
                """
                namespace App.Core.Orders { public sealed class SubmitUseCase { } }
                namespace App.Core.Billing { public sealed class SubmitUseCase { } }
                """),
            ("App.Tests",
                """
                namespace App.Tests.Orders;
                public sealed class SubmitUseCaseTests { }
                """));

        TestCoverageResult coverage = index.GetTestCoverage();

        coverage.CoveredUseCases.Should().Be(1);
        coverage.Uncovered.Should().ContainSingle().Which.Namespace.Should().Be("App.Core.Billing");
    }

    [Fact]
    public void GetTestCoverage_UniqueNameInDifferentTestNamespace_IsCovered()
    {
        RoslynWorkspaceIndex index = TestIndex.Create(
            ("App.Core", "namespace App.Core.Orders { public sealed class PlaceOrderUseCase { } }"),
            ("App.Tests", "namespace App.Tests; public sealed class PlaceOrderUseCaseTests { }"));

        index.GetTestCoverage().CoveredUseCases.Should().Be(1);
    }
}
