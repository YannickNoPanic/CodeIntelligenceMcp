using CodeIntelligenceMcp.Roslyn;
using CodeIntelligenceMcp.Roslyn.Models;
using FluentAssertions;
using Xunit;

namespace CodeIntelligenceMcp.Tests;

public sealed class ChangeAnalyzerSignatureTests
{
    private const string Source = """
        public class Svc
        {
            public int Run(
                [System.ComponentModel.Description("x")] string name,
                int count = 1) => 0;
        }
        """;

    [Fact]
    public void CompareSignatures_OnlyLineEndingsDiffer_ReportsNothing()
    {
        IEnumerable<SignatureChange> changes = ChangeAnalyzer.CompareSignatures(
            Source.Replace("\r\n", "\n"),
            Source.Replace("\r\n", "\n").Replace("\n", "\r\n"));

        changes.Should().BeEmpty();
    }

    [Fact]
    public void CompareSignatures_ReturnTypeChanged_ReportsCompactModifiedSignature()
    {
        SignatureChange change = ChangeAnalyzer.CompareSignatures(Source, Source.Replace("public int Run", "public long Run")).Single();

        change.ChangeKind.Should().Be("modified");
        change.MemberName.Should().Be("Run(string name, int count = 1)");
        change.AfterSignature.Should().Be("long Run(string name, int count = 1)");
    }
}
