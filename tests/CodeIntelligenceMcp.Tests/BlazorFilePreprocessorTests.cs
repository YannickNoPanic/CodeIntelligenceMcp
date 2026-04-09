using CodeIntelligenceMcp.Roslyn;
using FluentAssertions;
using Xunit;

namespace CodeIntelligenceMcp.Tests;

public sealed class BlazorFilePreprocessorTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    public BlazorFilePreprocessorTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        Directory.Delete(_tempDir, recursive: true);
    }

    private string WriteRazor(string content)
    {
        string path = Path.Combine(_tempDir, Path.GetRandomFileName() + ".razor");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void ExtractCodeBlock_ReturnsNull_WhenNoCodeBlock()
    {
        string path = WriteRazor("<h1>Hello</h1>\n<p>No code here</p>");

        BlazorCodeBlock? result = BlazorFilePreprocessor.ExtractCodeBlock(path);

        result.Should().BeNull();
    }

    [Fact]
    public void ExtractCodeBlock_ReturnsInnerContent_ForSimpleCodeBlock()
    {
        string path = WriteRazor(
            "<h1>Hello</h1>\n" +
            "@code {\n" +
            "    private int _count = 0;\n" +
            "}");

        BlazorCodeBlock? result = BlazorFilePreprocessor.ExtractCodeBlock(path);

        result.Should().NotBeNull();
        result!.Source.Should().Contain("_count");
    }

    [Fact]
    public void ExtractCodeBlock_LineOffset_IsOneBasedLineOfOpeningBrace()
    {
        // @code { is on line 3 (1-based)
        string path = WriteRazor(
            "<h1>Hello</h1>\n" +
            "<p>World</p>\n" +
            "@code {\n" +
            "    private int _x;\n" +
            "}");

        BlazorCodeBlock? result = BlazorFilePreprocessor.ExtractCodeBlock(path);

        result.Should().NotBeNull();
        result!.LineOffset.Should().Be(3);
    }

    [Fact]
    public void ExtractCodeBlock_HandlesNestedBraces()
    {
        string path = WriteRazor(
            "@code {\n" +
            "    private void Foo() { int x = 1; }\n" +
            "    private void Bar() { if (true) { } }\n" +
            "}");

        BlazorCodeBlock? result = BlazorFilePreprocessor.ExtractCodeBlock(path);

        result.Should().NotBeNull();
        result!.Source.Should().Contain("Foo");
        result.Source.Should().Contain("Bar");
    }

    [Fact]
    public void ExtractCodeBlock_ReturnsNull_WhenAtCodeHasNoOpeningBrace()
    {
        string path = WriteRazor("<h1>Hello</h1>\n@code");

        BlazorCodeBlock? result = BlazorFilePreprocessor.ExtractCodeBlock(path);

        result.Should().BeNull();
    }

    [Fact]
    public void ExtractCodeBlock_ExcludesOuterBraces_FromSource()
    {
        string path = WriteRazor("@code {\n    private int _x;\n}");

        BlazorCodeBlock? result = BlazorFilePreprocessor.ExtractCodeBlock(path);

        result.Should().NotBeNull();
        result!.Source.Should().NotStartWith("{");
        result.Source.Should().NotEndWith("}");
    }
}
