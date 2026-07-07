using System.Text.RegularExpressions;

namespace CodeIntelligenceMcp.Roslyn;

// Glob-style matching for symbol queries: '*' matches any run, '?' one character.
// Queries without wildcards fall back to case-insensitive substring, the historical behavior.
// Duplicated as NameMatcher in CodeIntelligenceMcp.Common for the file-walk indexers —
// this project deliberately has no dependency on Common.
public static class SymbolQueryMatcher
{
    public static bool HasWildcards(string query) => query.Contains('*') || query.Contains('?');

    public static bool Matches(string query, string candidate)
    {
        if (!HasWildcards(query))
            return candidate.Contains(query, StringComparison.OrdinalIgnoreCase);

        string pattern = "^" + Regex.Escape(query).Replace("\\*", ".*").Replace("\\?", ".") + "$";
        return Regex.IsMatch(candidate, pattern, RegexOptions.IgnoreCase);
    }
}
