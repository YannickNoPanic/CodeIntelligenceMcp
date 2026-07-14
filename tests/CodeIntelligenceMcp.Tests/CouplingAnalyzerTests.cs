using CodeIntelligenceMcp.Roslyn;
using CodeIntelligenceMcp.Roslyn.Models;
using FluentAssertions;
using Xunit;

namespace CodeIntelligenceMcp.Tests;

public sealed class CouplingAnalyzerTests
{
    private const string WideSource =
        """
        namespace App.Core;
        public class DepA { } public class DepB { } public class DepC { }
        public class DepD { } public class DepE { } public class DepF { }
        public class Hub
        {
            public DepA? A { get; set; }
            public DepB? B { get; set; }
            public DepC? C { get; set; }
            public DepD Make(DepE e, DepF f) => new DepD();
        }
        """;

    [Fact]
    public void GetCoupling_TypeWithSixDependencies_ReportsCouplingSix()
    {
        RoslynWorkspaceIndex index = TestIndex.Create(("App.Core", WideSource));

        IReadOnlyList<TypeCoupling> results = new CouplingAnalyzer(index).GetCoupling(minCoupling: 5);

        results.Should().ContainSingle(r => r.TypeName == "Hub").Which.EfferentCoupling.Should().Be(6);
    }

    [Fact]
    public void GetCoupling_MinCouplingAboveActual_FiltersOut()
    {
        RoslynWorkspaceIndex index = TestIndex.Create(("App.Core", WideSource));

        IReadOnlyList<TypeCoupling> results = new CouplingAnalyzer(index).GetCoupling(minCoupling: 7);

        results.Should().BeEmpty();
    }

    [Fact]
    public void GetCoupling_TestProjects_AreSkipped()
    {
        RoslynWorkspaceIndex index = TestIndex.Create(("App.Tests", WideSource));

        IReadOnlyList<TypeCoupling> results = new CouplingAnalyzer(index).GetCoupling(minCoupling: 1);

        results.Should().BeEmpty();
    }
}
