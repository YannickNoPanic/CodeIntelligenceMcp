using CodeIntelligenceMcp.Roslyn;
using CodeIntelligenceMcp.Roslyn.Models;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace CodeIntelligenceMcp.Tests;

public sealed class TestProjectDetectorTests
{
    private static Compilation CompilationWith(string name, params Type[] referencedTypes) =>
        CSharpCompilation.Create(
            name,
            [CSharpSyntaxTree.ParseText("public class C {}")],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
             .. referencedTypes.Select(t => MetadataReference.CreateFromFile(t.Assembly.Location))]);

    [Theory]
    [InlineData("App.Tests")]
    [InlineData("App.UnitTests")]
    [InlineData("App.IntegrationTests")]
    [InlineData("App.Test")]
    public void IsTestProject_TestNamedProject_IsTest(string name)
    {
        TestProjectDetector.IsTestProject(name, CompilationWith(name)).Should().BeTrue();
    }

    [Theory]
    [InlineData("App.Core")]
    [InlineData("App.Contest")]
    [InlineData("App.Testing")]
    public void IsTestProject_ProductionProject_IsNotTest(string name)
    {
        TestProjectDetector.IsTestProject(name, CompilationWith(name)).Should().BeFalse();
    }

    [Fact]
    public void IsTestProject_ReferencesTestFramework_IsTestRegardlessOfName()
    {
        TestProjectDetector.IsTestProject("App.Specs", CompilationWith("App.Specs", typeof(FactAttribute))).Should().BeTrue();
    }

    [Fact]
    public async Task DetectAsync_MissingCancellationToken_SkipsUnitTestsProject()
    {
        RoslynWorkspaceIndex index = TestIndex.Create(("App.UnitTests",
            """
            using System.Threading.Tasks;
            namespace App.UnitTests;
            public class SvcTests
            {
                public async Task RunAsync() { await Task.Delay(1); }
            }
            """));

        IReadOnlyList<ViolationResult> violations =
            await new ViolationDetector(index, TestIndex.CleanArch).DetectAsync("missing-cancellation-token", CancellationToken.None);

        violations.Should().BeEmpty();
    }
}
