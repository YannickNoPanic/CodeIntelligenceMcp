using CodeIntelligenceMcp.AspClassic;
using CodeIntelligenceMcp.AspClassic.Models;
using FluentAssertions;
using Xunit;

namespace CodeIntelligenceMcp.Tests;

public sealed class AspBlockExtractorTests
{
    [Fact]
    public void Extract_ReturnsEmpty_WhenNoBlocks()
    {
        string content = "<html><body><h1>Hello</h1></body></html>";

        IReadOnlyList<VbscriptBlock> result = AspBlockExtractor.Extract(content);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Extract_ReturnsSingleBlock_ForSimpleBlock()
    {
        string content = "<html><%\r\nDim x\r\n%></html>";

        IReadOnlyList<VbscriptBlock> result = AspBlockExtractor.Extract(content);

        result.Should().HaveCount(1);
        result[0].Source.Should().Contain("Dim x");
    }

    [Fact]
    public void Extract_LineStart_IsOneBasedLineOfOpeningTag()
    {
        // <% starts on line 3
        string content = "<html>\r\n<body>\r\n<% Dim x %>\r\n</body>";

        IReadOnlyList<VbscriptBlock> result = AspBlockExtractor.Extract(content);

        result.Should().HaveCount(1);
        result[0].LineStart.Should().Be(3);
    }

    [Fact]
    public void Extract_StripsEqualsSign_FromOutputExpression()
    {
        string content = "<%= someVar %>";

        IReadOnlyList<VbscriptBlock> result = AspBlockExtractor.Extract(content);

        result.Should().HaveCount(1);
        result[0].Source.Should().Contain("someVar");
        result[0].Source.Should().NotStartWith("=");
    }

    [Fact]
    public void Extract_SkipsEmptyBlocks()
    {
        string content = "<%\r\n   \r\n%><% Dim x %>";

        IReadOnlyList<VbscriptBlock> result = AspBlockExtractor.Extract(content);

        result.Should().HaveCount(1);
        result[0].Source.Should().Contain("Dim x");
    }

    [Fact]
    public void Extract_PreservesLineNumbers_AcrossMultipleBlocks()
    {
        string content =
            "<html>\n" +         // line 1
            "<% Dim a %>\n" +    // line 2 — block 1
            "<p>text</p>\n" +    // line 3
            "<p>more</p>\n" +    // line 4
            "<% Dim b %>\n";     // line 5 — block 2

        IReadOnlyList<VbscriptBlock> result = AspBlockExtractor.Extract(content);

        result.Should().HaveCount(2);
        result[0].LineStart.Should().Be(2);
        result[1].LineStart.Should().Be(5);
    }

    [Fact]
    public void Extract_HandlesMultiLineBlock()
    {
        string content =
            "<%\n" +
            "Dim x\n" +
            "x = 42\n" +
            "%>";

        IReadOnlyList<VbscriptBlock> result = AspBlockExtractor.Extract(content);

        result.Should().HaveCount(1);
        result[0].LineStart.Should().Be(1);
        result[0].LineEnd.Should().Be(4);
        result[0].Source.Should().Contain("Dim x");
        result[0].Source.Should().Contain("x = 42");
    }

    [Fact]
    public void Extract_PercentInsideBlock_NotConfusedWithClosingTag()
    {
        // A % that isn't followed by > should remain in the source
        string content = "<% Dim pct : pct = 50 % 10 %>";

        IReadOnlyList<VbscriptBlock> result = AspBlockExtractor.Extract(content);

        result.Should().HaveCount(1);
        result[0].Source.Should().Contain("50 % 10");
    }
}
