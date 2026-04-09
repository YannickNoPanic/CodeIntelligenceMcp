using System.Text.RegularExpressions;
using CodeIntelligenceMcp.AspClassic.Models;

namespace CodeIntelligenceMcp.AspClassic;

public static class SqlExtractor
{
    private static readonly Regex AssignmentPattern = new(
        @"^\s*\w+\s*=\s*""(SELECT|INSERT|UPDATE|DELETE)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex InlineVariablePattern = new(
        @"""\s*&\s*(\w+)\s*&\s*""",
        RegexOptions.Compiled);

    private static readonly Regex TrailingVariablePattern = new(
        @"""\s*&\s*(\w+)\s*$",
        RegexOptions.Compiled);

    private static readonly Regex LeadingVariablePattern = new(
        @"^\s*&\s*(\w+)\s*&\s*""",
        RegexOptions.Compiled);

    private static readonly Regex TablePattern = new(
        @"(?:FROM|JOIN|INTO|UPDATE)\s+(?:\[?\w+\]?\.){0,2}\[?(\w+)\]?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ColumnPattern = new(
        @"\[?(\w+)\]?",
        RegexOptions.Compiled);

    private static readonly Regex PlaceholderPattern = new(
        @"\{(\w+)\}",
        RegexOptions.Compiled);

    private static readonly Regex WhitespacePattern = new(
        @"[\s]+",
        RegexOptions.Compiled);

    public static IReadOnlyList<SqlQueryInfo> Extract(string vbscriptSource, int lineOffset)
    {
        var results = new List<SqlQueryInfo>();
        string[] lines = vbscriptSource.Split('\n');

        int i = 0;
        while (i < lines.Length)
        {
            string line = lines[i];

            if (!AssignmentPattern.IsMatch(line))
            {
                i++;
                continue;
            }

            int queryStartIndex = i;
            var queryParts = new List<string>();

            string firstPart = ExtractFirstStringContent(line);
            bool continues = line.TrimEnd().EndsWith("&_", StringComparison.Ordinal);

            queryParts.Add(firstPart);
            i++;

            while (continues && i < lines.Length)
            {
                string continuationLine = lines[i];
                continues = continuationLine.TrimEnd().EndsWith("&_", StringComparison.Ordinal);
                queryParts.Add(ExtractContinuationContent(continuationLine));
                i++;
            }

            string rawQuery = string.Join(" ", queryParts);
            string normalised = NormaliseQuery(rawQuery);
            normalised = WhitespacePattern.Replace(normalised, " ").Trim();

            string operation = ExtractOperation(normalised);
            IReadOnlyList<string> tables = ExtractTables(normalised);
            IReadOnlyList<string> columns = ExtractColumns(normalised, operation);
            IReadOnlyList<string> parameters = ExtractParameters(normalised);

            int lineStart = lineOffset + queryStartIndex;
            int lineEnd = lineOffset + i - 1;

            results.Add(new SqlQueryInfo(
                lineStart,
                lineEnd,
                operation,
                tables,
                columns,
                normalised,
                parameters));
        }

        return results;
    }

    private static string ExtractFirstStringContent(string line)
    {
        int quoteIndex = line.IndexOf('"');
        if (quoteIndex < 0)
            return string.Empty;

        string afterFirstQuote = line.Substring(quoteIndex + 1);

        string withoutContinuation = afterFirstQuote.TrimEnd();
        if (withoutContinuation.EndsWith("\"&_", StringComparison.Ordinal))
        {
            withoutContinuation = withoutContinuation.Substring(0, withoutContinuation.Length - 3);
        }
        else if (withoutContinuation.EndsWith("&_", StringComparison.Ordinal))
        {
            withoutContinuation = withoutContinuation.Substring(0, withoutContinuation.Length - 2).TrimEnd();
            if (withoutContinuation.EndsWith('"'))
            {
                withoutContinuation = withoutContinuation.Substring(0, withoutContinuation.Length - 1);
            }
        }
        else if (withoutContinuation.EndsWith('"'))
        {
            withoutContinuation = withoutContinuation.Substring(0, withoutContinuation.Length - 1);
        }

        return withoutContinuation;
    }

    private static string ExtractContinuationContent(string line)
    {
        string trimmed = line.Trim();

        string withoutContinuation = trimmed.TrimEnd();
        if (withoutContinuation.EndsWith("\"&_", StringComparison.Ordinal))
        {
            withoutContinuation = withoutContinuation.Substring(0, withoutContinuation.Length - 3);
        }
        else if (withoutContinuation.EndsWith("&_", StringComparison.Ordinal))
        {
            withoutContinuation = withoutContinuation.Substring(0, withoutContinuation.Length - 2).TrimEnd();
            if (withoutContinuation.EndsWith('"'))
            {
                withoutContinuation = withoutContinuation.Substring(0, withoutContinuation.Length - 1);
            }
        }
        else if (withoutContinuation.EndsWith('"'))
        {
            withoutContinuation = withoutContinuation.Substring(0, withoutContinuation.Length - 1);
        }

        if (withoutContinuation.StartsWith('"'))
        {
            withoutContinuation = withoutContinuation.Substring(1);
        }
        else if (withoutContinuation.StartsWith("& ", StringComparison.Ordinal) || withoutContinuation.StartsWith("&", StringComparison.Ordinal))
        {
            int ampPos = withoutContinuation.IndexOf('&');
            withoutContinuation = withoutContinuation.Substring(ampPos + 1).TrimStart();
            if (withoutContinuation.StartsWith('"'))
            {
                withoutContinuation = withoutContinuation.Substring(1);
            }
        }

        return withoutContinuation;
    }

    private static string NormaliseQuery(string raw)
    {
        string result = InlineVariablePattern.Replace(raw, m => "{" + m.Groups[1].Value + "}");
        result = TrailingVariablePattern.Replace(result, m => "{" + m.Groups[1].Value + "}");
        result = LeadingVariablePattern.Replace(result, m => "{" + m.Groups[1].Value + "}");
        return result;
    }

    private static string ExtractOperation(string query)
    {
        string trimmed = query.TrimStart();
        int spaceIndex = trimmed.IndexOf(' ');
        string word = spaceIndex < 0 ? trimmed : trimmed.Substring(0, spaceIndex);
        return word.ToUpperInvariant();
    }

    private static IReadOnlyList<string> ExtractTables(string query)
    {
        var tables = new List<string>();
        foreach (Match match in TablePattern.Matches(query))
        {
            string tableName = match.Groups[1].Value;
            if (!tables.Contains(tableName, StringComparer.OrdinalIgnoreCase))
            {
                tables.Add(tableName);
            }
        }

        return tables;
    }

    private static IReadOnlyList<string> ExtractColumns(string query, string operation)
    {
        if (!operation.Equals("SELECT", StringComparison.OrdinalIgnoreCase))
            return Array.Empty<string>();

        int selectIndex = query.IndexOf("SELECT", StringComparison.OrdinalIgnoreCase);
        if (selectIndex < 0)
            return Array.Empty<string>();

        int fromIndex = query.IndexOf(" FROM ", StringComparison.OrdinalIgnoreCase);
        if (fromIndex < 0)
            return Array.Empty<string>();

        string selectClause = query.Substring(selectIndex + 6, fromIndex - selectIndex - 6).Trim();

        if (selectClause == "*")
            return Array.Empty<string>();

        var columns = new List<string>();
        foreach (string part in selectClause.Split(','))
        {
            string cleaned = part.Trim();
            if (cleaned == "*")
                continue;

            Match m = ColumnPattern.Match(cleaned);
            if (m.Success)
            {
                string colName = m.Groups[1].Value;
                if (!string.IsNullOrWhiteSpace(colName) && !columns.Contains(colName, StringComparer.OrdinalIgnoreCase))
                {
                    columns.Add(colName);
                }
            }
        }

        return columns;
    }

    private static IReadOnlyList<string> ExtractParameters(string query)
    {
        var parameters = new List<string>();
        foreach (Match match in PlaceholderPattern.Matches(query))
        {
            string name = match.Groups[1].Value;
            if (!parameters.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                parameters.Add(name);
            }
        }

        return parameters;
    }
}
