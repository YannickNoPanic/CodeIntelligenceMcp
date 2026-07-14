using CodeIntelligenceMcp.Roslyn;
using CodeIntelligenceMcp.Roslyn.Models;
using FluentAssertions;
using Xunit;

namespace CodeIntelligenceMcp.Tests;

public sealed class PatternScannerTests
{
    [Fact]
    public async Task ScanAsync_CountsTypesInterfacesAndUseCases()
    {
        RoslynWorkspaceIndex index = TestIndex.Create(("App.Core",
            """
            namespace App.Core;
            public interface IUseCase<TIn> { }
            public interface IOrderQueries { }
            public sealed class CreateOrderUseCase : IUseCase<int> { }
            public class OrderService { }
            """));

        PatternSummary summary = await new PatternScanner(index, TestIndex.CleanArch).ScanAsync();

        summary.StructuralSummary.TotalTypes.Should().Be(4);
        summary.StructuralSummary.Interfaces.Should().Be(2);
        summary.StructuralSummary.UseCases.Should().Be(1);
    }

    [Fact]
    public async Task ScanAsync_UnsealedUseCase_ShowsUpInObservations()
    {
        RoslynWorkspaceIndex index = TestIndex.Create(("App.Core",
            """
            namespace App.Core;
            public interface IUseCase<TIn> { }
            public class CreateOrderUseCase : IUseCase<int> { }
            """));

        PatternSummary summary = await new PatternScanner(index, TestIndex.CleanArch).ScanAsync();

        summary.Observations.Should().Contain(o => o.Contains("0 sealed") && o.Contains("1 not sealed"));
    }
}
