using CodeIntelligenceMcp.AspClassic;
using CodeIntelligenceMcp.AspClassic.Models;
using FluentAssertions;
using Xunit;

namespace CodeIntelligenceMcp.Tests;

public sealed class SqlExtractorTests
{
    [Fact]
    public void Extract_ReturnsEmpty_WhenNoSql()
    {
        string source = "Dim x\r\nx = 42\r\nResponse.Write x";

        IReadOnlyList<SqlQueryInfo> result = SqlExtractor.Extract(source, lineOffset: 1);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Extract_DetectsSimpleSelect()
    {
        string source = "sql = \"SELECT id, name FROM dbo.Users\"";

        IReadOnlyList<SqlQueryInfo> result = SqlExtractor.Extract(source, lineOffset: 1);

        result.Should().HaveCount(1);
        result[0].Operation.Should().Be("SELECT");
    }

    [Fact]
    public void Extract_DetectsInsert()
    {
        string source = "cmd = \"INSERT INTO dbo.Orders (name) VALUES ('x')\"";

        IReadOnlyList<SqlQueryInfo> result = SqlExtractor.Extract(source, lineOffset: 1);

        result.Should().HaveCount(1);
        result[0].Operation.Should().Be("INSERT");
    }

    [Fact]
    public void Extract_DetectsUpdate()
    {
        string source = "s = \"UPDATE dbo.Users SET name = 'x' WHERE id = 1\"";

        IReadOnlyList<SqlQueryInfo> result = SqlExtractor.Extract(source, lineOffset: 1);

        result.Should().HaveCount(1);
        result[0].Operation.Should().Be("UPDATE");
    }

    [Fact]
    public void Extract_DetectsDelete()
    {
        string source = "s = \"DELETE FROM dbo.Orders WHERE id = 1\"";

        IReadOnlyList<SqlQueryInfo> result = SqlExtractor.Extract(source, lineOffset: 1);

        result.Should().HaveCount(1);
        result[0].Operation.Should().Be("DELETE");
    }

    [Fact]
    public void Extract_ExtractsTablesFromFrom()
    {
        string source = "sql = \"SELECT id FROM dbo.Users\"";

        IReadOnlyList<SqlQueryInfo> result = SqlExtractor.Extract(source, lineOffset: 1);

        result[0].Tables.Should().Contain("Users");
    }

    [Fact]
    public void Extract_ExtractsTablesFromJoin()
    {
        string source = "sql = \"SELECT u.id FROM dbo.Users u JOIN dbo.Orders o ON u.id = o.userId\"";

        IReadOnlyList<SqlQueryInfo> result = SqlExtractor.Extract(source, lineOffset: 1);

        result[0].Tables.Should().Contain("Users");
        result[0].Tables.Should().Contain("Orders");
    }

    [Fact]
    public void Extract_ExtractsColumnsFromSelect()
    {
        string source = "sql = \"SELECT [id], [name], [email] FROM dbo.Users\"";

        IReadOnlyList<SqlQueryInfo> result = SqlExtractor.Extract(source, lineOffset: 1);

        result[0].Columns.Should().Contain("id");
        result[0].Columns.Should().Contain("name");
        result[0].Columns.Should().Contain("email");
    }

    [Fact]
    public void Extract_ReturnsEmptyColumns_ForSelectStar()
    {
        string source = "sql = \"SELECT * FROM dbo.Users\"";

        IReadOnlyList<SqlQueryInfo> result = SqlExtractor.Extract(source, lineOffset: 1);

        result[0].Columns.Should().BeEmpty();
    }

    [Fact]
    public void Extract_ReplacesInlineVariable_WithPlaceholder()
    {
        string source = "sql = \"SELECT id FROM dbo.Users WHERE name = '\" & v_Name & \"'\"";

        IReadOnlyList<SqlQueryInfo> result = SqlExtractor.Extract(source, lineOffset: 1);

        result.Should().HaveCount(1);
        result[0].Signature.Should().Contain("{v_Name}");
        result[0].Parameters.Should().Contain("v_Name");
    }

    [Fact]
    public void Extract_FollowsContinuationLines()
    {
        string source =
            "sql = \"SELECT [id],[name] \" &_\r\n" +
            "      \"FROM [dbo].[Users] \" &_\r\n" +
            "      \"WHERE [id] = 1\"";

        IReadOnlyList<SqlQueryInfo> result = SqlExtractor.Extract(source, lineOffset: 1);

        result.Should().HaveCount(1);
        result[0].Signature.Should().Contain("SELECT");
        result[0].Signature.Should().Contain("FROM");
        result[0].Signature.Should().Contain("WHERE");
        result[0].Tables.Should().Contain("Users");
    }

    [Fact]
    public void Extract_LineStart_RespectsOffset()
    {
        // The SQL assignment is on line 3 of the block; block starts at lineOffset=10
        string source = "Dim x\r\nx = 1\r\nsql = \"SELECT id FROM dbo.Users\"";

        IReadOnlyList<SqlQueryInfo> result = SqlExtractor.Extract(source, lineOffset: 10);

        result.Should().HaveCount(1);
        result[0].LineStart.Should().Be(12); // lineOffset(10) + line index(2, 0-based) = 12
    }

    [Fact]
    public void Extract_MultipleQueries_InSameBlock()
    {
        string source =
            "sql1 = \"SELECT id FROM dbo.Users\"\r\n" +
            "sql2 = \"INSERT INTO dbo.Log (msg) VALUES ('x')\"";

        IReadOnlyList<SqlQueryInfo> result = SqlExtractor.Extract(source, lineOffset: 1);

        result.Should().HaveCount(2);
        result.Should().Contain(q => q.Operation == "SELECT");
        result.Should().Contain(q => q.Operation == "INSERT");
    }
}
