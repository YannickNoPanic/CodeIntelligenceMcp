using CodeIntelligenceMcp.Roslyn;
using CodeIntelligenceMcp.Roslyn.Models;
using FluentAssertions;
using Xunit;

namespace CodeIntelligenceMcp.Tests;

// GetChangeRiskAsync needs a Solution (SymbolFinder) and is covered by the integration
// fixture; hotspot scoring on coupling and test presence works on the in-memory index.
public sealed class RiskAnalyzerTests
{
    private const string HubSource =
        """
        namespace App.Core;
        public class DepA { } public class DepB { } public class DepC { }
        public class DepD { } public class DepE { } public class DepF { }
        public class DepG { } public class DepH { } public class DepI { } public class DepJ { }
        public class Hub
        {
            public DepA? A { get; set; }
            public DepB? B { get; set; }
            public DepC? C { get; set; }
            public DepD? D { get; set; }
            public DepE? E { get; set; }
            public DepF? F { get; set; }
            public DepG? G { get; set; }
            public DepH? H { get; set; }
            public DepI? I { get; set; }
            public DepJ? J { get; set; }
        }
        """;

    [Fact]
    public async Task GetHotspotsAsync_UntestedHighCouplingType_ScoresHigherThanTestedTwin()
    {
        RoslynWorkspaceIndex untested = TestIndex.Create(("App.Core", HubSource));
        RoslynWorkspaceIndex tested = TestIndex.Create(
            ("App.Core", HubSource),
            ("App.Tests", "namespace App.Tests; public class HubTests { }"));

        IReadOnlyList<HotspotResult> untestedHotspots = await new RiskAnalyzer(untested).GetHotspotsAsync();
        IReadOnlyList<HotspotResult> testedHotspots = await new RiskAnalyzer(tested).GetHotspotsAsync();

        int untestedScore = untestedHotspots.Single(h => h.TypeName == "Hub").HotspotScore;
        int testedScore = testedHotspots.Single(h => h.TypeName == "Hub").HotspotScore;
        untestedScore.Should().BeGreaterThan(testedScore);
    }

    [Fact]
    public async Task GetHotspotsAsync_HighCouplingType_ReportsCouplingInReason()
    {
        RoslynWorkspaceIndex index = TestIndex.Create(("App.Core", HubSource));

        IReadOnlyList<HotspotResult> hotspots = await new RiskAnalyzer(index).GetHotspotsAsync();

        HotspotResult hub = hotspots.Single(h => h.TypeName == "Hub");
        hub.Coupling.Should().Be(10);
        hub.Reason.Should().Contain("coupling").And.Contain("no tests");
    }
}
