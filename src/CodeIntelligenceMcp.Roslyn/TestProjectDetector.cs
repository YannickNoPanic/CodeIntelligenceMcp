using Microsoft.CodeAnalysis;

namespace CodeIntelligenceMcp.Roslyn;

// Test project = references a test framework, or last name segment ends in Test/Tests (App.UnitTests).
public static class TestProjectDetector
{
    private static readonly HashSet<string> TestFrameworkAssemblies = new(StringComparer.OrdinalIgnoreCase)
    {
        "xunit.core",
        "xunit.v3.core",
        "nunit.framework",
        "Microsoft.VisualStudio.TestPlatform.TestFramework",
        "MSTest.TestFramework",
        "TUnit.Core"
    };

    public static bool IsTestProject(string projectName, Compilation? compilation) =>
        HasTestName(projectName)
        || (compilation is not null && compilation.ReferencedAssemblyNames.Any(a => TestFrameworkAssemblies.Contains(a.Name)));

    private static bool HasTestName(string projectName)
    {
        string segment = projectName[(projectName.LastIndexOf('.') + 1)..];
        return segment.EndsWith("Tests", StringComparison.Ordinal)
            || segment.EndsWith("Test", StringComparison.Ordinal)
            || segment.Equals("tests", StringComparison.OrdinalIgnoreCase);
    }
}
