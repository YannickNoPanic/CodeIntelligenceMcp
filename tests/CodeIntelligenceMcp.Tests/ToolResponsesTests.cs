using System.Text.Json;
using CodeIntelligenceMcp.Tools;
using FluentAssertions;
using Xunit;

namespace CodeIntelligenceMcp.Tests;

public sealed class ToolResponsesTests
{
    [Fact]
    public void OkList_MoreItemsThanMax_TruncatesAndReportsTotal()
    {
        IReadOnlyList<int> items = [.. Enumerable.Range(1, 250)];

        string json = ToolResponses.OkList(items, maxResults: 100);

        using JsonDocument doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("total").GetInt32().Should().Be(250);
        doc.RootElement.GetProperty("returned").GetInt32().Should().Be(100);
        doc.RootElement.GetProperty("truncated").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("items").GetArrayLength().Should().Be(100);
        doc.RootElement.GetProperty("hint").GetString().Should().Contain("maxResults");
    }

    [Fact]
    public void OkList_FewerItemsThanMax_NoTruncationFieldsNoise()
    {
        string json = ToolResponses.OkList<int>([1, 2], maxResults: 100);

        using JsonDocument doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("truncated").GetBoolean().Should().BeFalse();
        doc.RootElement.TryGetProperty("hint", out _).Should().BeFalse();
        doc.RootElement.TryGetProperty("stale", out _).Should().BeFalse();
    }

    [Fact]
    public void OkList_ZeroMaxResults_ReturnsEverything()
    {
        IReadOnlyList<int> items = [.. Enumerable.Range(1, 250)];

        string json = ToolResponses.OkList(items, maxResults: 0);

        using JsonDocument doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("truncated").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("items").GetArrayLength().Should().Be(250);
    }

    [Fact]
    public void Err_QuotesInMessage_AreNotUnicodeEscaped()
    {
        string json = ToolResponses.Err("workspace 'x' not found");

        json.Should().Contain("'x'").And.NotContain("\\u0027");
    }

    [Fact]
    public void Err_NoHintNoDetail_OmitsNullFields()
    {
        string json = ToolResponses.Err("boom");

        using JsonDocument doc = JsonDocument.Parse(json);
        doc.RootElement.TryGetProperty("hint", out _).Should().BeFalse();
        doc.RootElement.TryGetProperty("detail", out _).Should().BeFalse();
    }

    [Fact]
    public void Ok_Stale_WrapsResultWithHint()
    {
        string json = ToolResponses.Ok(new { value = 1 }, stale: true);

        using JsonDocument doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("stale").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("result").GetProperty("value").GetInt32().Should().Be(1);
        doc.RootElement.GetProperty("hint").GetString().Should().Contain("refresh_workspace");
    }

    [Fact]
    public void Ok_NotStale_SerializesResultDirectly()
    {
        string json = ToolResponses.Ok(new { value = 1 });

        using JsonDocument doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("value").GetInt32().Should().Be(1);
        doc.RootElement.TryGetProperty("stale", out _).Should().BeFalse();
    }
}
