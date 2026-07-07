using System.Text.RegularExpressions;

namespace CodeIntelligenceMcp.Common;

// Glob-style matching for name queries: '*' matches any run, '?' one character.
// Queries without wildcards fall back to case-insensitive substring, the historical behavior.
// Mirrors SymbolQueryMatcher in CodeIntelligenceMcp.Roslyn, which cannot depend on this project.
public static class NameMatcher
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
