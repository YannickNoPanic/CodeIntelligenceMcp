namespace CodeIntelligenceMcp.Roslyn;

public record BlazorCodeBlock(string Source, int LineOffset);

public static class BlazorFilePreprocessor
{
    public static BlazorCodeBlock? ExtractCodeBlock(string filePath)
    {
        string content = File.ReadAllText(filePath);
        string[] lines = content.Split('\n');

        int codeKeywordIndex = content.IndexOf("@code", StringComparison.Ordinal);
        if (codeKeywordIndex < 0)
            return null;

        int searchFrom = codeKeywordIndex + "@code".Length;
        int openBrace = -1;
        for (int i = searchFrom; i < content.Length; i++)
        {
            if (content[i] == '{')
            {
                openBrace = i;
                break;
            }

            if (!char.IsWhiteSpace(content[i]))
                return null;
        }

        if (openBrace < 0)
            return null;

        int lineOffset = CountLines(content, 0, openBrace) + 1;

        int depth = 1;
        int start = openBrace + 1;
        int pos = start;
        while (pos < content.Length && depth > 0)
        {
            char c = content[pos];
            if (c == '{')
                depth++;
            else if (c == '}')
                depth--;
            pos++;
        }

        if (depth != 0)
            return null;

        string innerContent = content.Substring(start, pos - start - 1);
        return new BlazorCodeBlock(innerContent, lineOffset);
    }

    private static int CountLines(string text, int from, int to)
    {
        int count = 0;
        for (int i = from; i < to && i < text.Length; i++)
        {
            if (text[i] == '\n')
                count++;
        }

        return count;
    }
}
