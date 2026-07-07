namespace CodeIntelligenceMcp.Roslyn;

// Shared between ViolationDetector (whole-workspace rules) and FileAnalyzer (single-file
// analysis) so the two can never drift apart on what Core is allowed to reference.
internal static class ForbiddenCoreNamespaces
{
    internal static bool IsEfCore(string ns) =>
        ns.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.OrdinalIgnoreCase);

    internal static bool IsHttp(string ns) =>
        ns.Equals("System.Net.Http", StringComparison.OrdinalIgnoreCase)
        || ns.Contains("IHttpClientFactory", StringComparison.OrdinalIgnoreCase);

    internal static bool IsAzure(string ns) =>
        ns.StartsWith("Azure.", StringComparison.OrdinalIgnoreCase)
        || ns.StartsWith("Microsoft.Azure.", StringComparison.OrdinalIgnoreCase);
}
