using CodeIntelligenceMcp.AspClassic;
using CodeIntelligenceMcp.AspClassic.Models;
using FluentAssertions;
using Xunit;

namespace CodeIntelligenceMcp.Tests;

public sealed class AspIndexTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("citest-asp").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); }
        catch (IOException) { }
    }

    private AspIndex BuildWithQueryFile()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "pages"));
        File.WriteAllText(Path.Combine(_dir, "pages", "list.asp"), "<%\nsql = \"SELECT Id FROM Users\"\n%>");
        return AspIndex.Build(_dir);
    }

    [Theory]
    [InlineData("pages/list.asp")]
    [InlineData("pages\\list.asp")]
    public void GetFileQueries_RelativePath_FindsQueries(string relativePath)
    {
        IReadOnlyList<SqlQueryInfo>? queries = BuildWithQueryFile().GetFileQueries(relativePath);

        queries.Should().NotBeNull().And.NotBeEmpty();
    }

    [Fact]
    public void GetFileQueries_AbsolutePath_FindsQueries()
    {
        IReadOnlyList<SqlQueryInfo>? queries = BuildWithQueryFile().GetFileQueries(Path.Combine(_dir, "pages", "list.asp"));

        queries.Should().NotBeNull().And.NotBeEmpty();
    }

    [Fact]
    public void GetFileQueries_UnknownFile_ReturnsNull()
    {
        BuildWithQueryFile().GetFileQueries("pages/missing.asp").Should().BeNull();
    }
}
