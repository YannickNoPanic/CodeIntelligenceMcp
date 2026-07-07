using CodeIntelligenceMcp.Python;
using CodeIntelligenceMcp.Python.Models;
using FluentAssertions;
using Xunit;

namespace CodeIntelligenceMcp.Tests;

public sealed class PythonFileParserTests
{
    [Fact]
    public void Parse_SimpleFunction_ExtractsNameAndLineNumber()
    {
        string content = """
            def get_status():
                return "ok"
            """;

        PythonFileInfo result = PythonFileParser.Parse("test.py", content);

        result.Functions.Should().HaveCount(1);
        result.Functions[0].Name.Should().Be("get_status");
        result.Functions[0].LineStart.Should().Be(1);
    }

    [Fact]
    public void Parse_AsyncFunction_DetectsAsync()
    {
        string content = """
            async def fetch_data():
                pass
            """;

        PythonFileInfo result = PythonFileParser.Parse("test.py", content);

        result.Functions.Should().HaveCount(1);
        result.Functions[0].Name.Should().Be("fetch_data");
        result.Functions[0].IsAsync.Should().BeTrue();
    }

    [Fact]
    public void Parse_FunctionWithTypedParameters_ExtractsParametersAndReturnHint()
    {
        string content = """
            def greet(name: str, count: int = 1) -> str:
                return name * count
            """;

        PythonFileInfo result = PythonFileParser.Parse("test.py", content);

        PythonFunctionInfo func = result.Functions[0];
        func.Parameters.Should().HaveCount(2);
        func.Parameters[0].Name.Should().Be("name");
        func.Parameters[0].TypeHint.Should().Be("str");
        func.Parameters[1].Name.Should().Be("count");
        func.Parameters[1].DefaultValue.Should().Be("1");
        func.ReturnTypeHint.Should().Be("str");
    }

    [Fact]
    public void Parse_ClassWithMethodsAndBases_ExtractsClassDetails()
    {
        string content = """
            class UserRepository(BaseRepository, LoggingMixin):
                def get(self, user_id: int):
                    pass

                async def fetch_all(self):
                    pass
            """;

        PythonFileInfo result = PythonFileParser.Parse("test.py", content);

        result.Classes.Should().HaveCount(1);
        PythonClassInfo cls = result.Classes[0];
        cls.Name.Should().Be("UserRepository");
        cls.LineStart.Should().Be(1);
        cls.BaseClasses.Should().Equal("BaseRepository", "LoggingMixin");
        cls.Methods.Should().HaveCount(2);
        cls.Methods.Select(m => m.Name).Should().Contain(["get", "fetch_all"]);
        cls.Methods.Should().OnlyContain(m => m.IsMethod);
    }

    [Fact]
    public void Parse_SimpleImports_ExtractsModules()
    {
        string content = """
            import os
            import sys, json
            """;

        PythonFileInfo result = PythonFileParser.Parse("test.py", content);

        result.Imports.Should().HaveCount(3);
        result.Imports.Select(i => i.Module).Should().Contain(["os", "sys", "json"]);
        result.Imports[0].Line.Should().Be(1);
    }

    [Fact]
    public void Parse_FromImports_ExtractsModuleAndNames()
    {
        string content = """
            from pathlib import Path, PurePath
            from .models import User
            """;

        PythonFileInfo result = PythonFileParser.Parse("test.py", content);

        result.Imports.Should().HaveCount(2);
        result.Imports[0].Module.Should().Be("pathlib");
        result.Imports[0].Names.Should().Equal("Path", "PurePath");
        result.Imports[0].IsRelative.Should().BeFalse();
        result.Imports[1].Module.Should().Be(".models");
        result.Imports[1].IsRelative.Should().BeTrue();
    }

    [Fact]
    public void Parse_DecoratedFunction_ExtractsDecorators()
    {
        string content = """
            @app.route("/users")
            def get_users():
                pass
            """;

        PythonFileInfo result = PythonFileParser.Parse("test.py", content);

        result.Functions.Should().HaveCount(1);
        result.Functions[0].Decorators.Should().ContainSingle()
            .Which.Should().Be("app.route(\"/users\")");
    }

    [Fact]
    public void Parse_DecoratedClass_ExtractsDecorators()
    {
        string content = """
            @dataclass
            class Point:
                def scale(self):
                    pass
            """;

        PythonFileInfo result = PythonFileParser.Parse("test.py", content);

        result.Classes.Should().HaveCount(1);
        result.Classes[0].Decorators.Should().ContainSingle()
            .Which.Should().Be("dataclass");
    }

    [Fact]
    public void Parse_DocstringContainingCode_DoesNotIndexPhantomSymbols()
    {
        string content = """"
            def real():
                """Example usage.

                def phantom():
                    pass

                import fake
                from fake import thing
                """
                return 1
            """";

        PythonFileInfo result = PythonFileParser.Parse("test.py", content);

        result.Functions.Should().ContainSingle()
            .Which.Name.Should().Be("real");
        result.Imports.Should().BeEmpty();
        result.Classes.Should().BeEmpty();
    }

    [Fact]
    public void Parse_SingleLineDocstring_DoesNotBreakSubsequentParsing()
    {
        string content = """"
            def first():
                """Single line docstring."""
                return 1

            def second():
                '''Also a single line.'''
                return 2
            """";

        PythonFileInfo result = PythonFileParser.Parse("test.py", content);

        result.Functions.Should().HaveCount(2);
        result.Functions.Select(f => f.Name).Should().Equal("first", "second");
    }

    [Fact]
    public void Parse_ModuleWithOnlyDocstring_ParsesCleanly()
    {
        string content = """"
            """This module does things.

            import os

            def helper():
                pass
            """
            """";

        PythonFileInfo result = PythonFileParser.Parse("test.py", content);

        result.Functions.Should().BeEmpty();
        result.Imports.Should().BeEmpty();
        result.Classes.Should().BeEmpty();
    }

    [Fact]
    public void Parse_PrefixedTripleQuoteOpeningAtEndOfLine_SkipsStringBody()
    {
        string content = """
            DOC = r'''
            def phantom():
                pass

            class Phantom:
                pass
            '''

            def real():
                pass
            """;

        PythonFileInfo result = PythonFileParser.Parse("test.py", content);

        result.Functions.Should().ContainSingle()
            .Which.Name.Should().Be("real");
        result.Classes.Should().BeEmpty();
    }

    [Fact]
    public void Parse_TripleQuoteOpeningAndClosingOnSameLine_ContinuesParsing()
    {
        string content = """"
            x = """single line"""

            def after():
                pass
            """";

        PythonFileInfo result = PythonFileParser.Parse("test.py", content);

        result.Functions.Should().ContainSingle()
            .Which.Name.Should().Be("after");
    }

    [Fact]
    public void Parse_EmptyContent_ReturnsEmptyCollections()
    {
        PythonFileInfo result = PythonFileParser.Parse("empty.py", string.Empty);

        result.Functions.Should().BeEmpty();
        result.Classes.Should().BeEmpty();
        result.Imports.Should().BeEmpty();
    }
}
