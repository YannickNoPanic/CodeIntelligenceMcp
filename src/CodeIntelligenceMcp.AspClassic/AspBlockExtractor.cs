using CodeIntelligenceMcp.AspClassic.Models;

namespace CodeIntelligenceMcp.AspClassic;

public static class AspBlockExtractor
{
    public static IReadOnlyList<VbscriptBlock> Extract(string fileContent)
    {
        var blocks = new List<VbscriptBlock>();
        int length = fileContent.Length;
        int currentLine = 1;
        int i = 0;

        while (i < length)
        {
            char c = fileContent[i];

            if (c == '<' && i + 1 < length && fileContent[i + 1] == '%')
            {
                int lineStart = currentLine;
                i += 2;

                bool isExpression = false;
                if (i < length && (fileContent[i] == '=' || fileContent[i] == '@'))
                {
                    isExpression = true;
                    i++;
                }

                var captured = new System.Text.StringBuilder();

                while (i < length)
                {
                    if (fileContent[i] == '%' && i + 1 < length && fileContent[i + 1] == '>')
                    {
                        i += 2;
                        break;
                    }

                    if (fileContent[i] == '\n')
                    {
                        currentLine++;
                    }

                    captured.Append(fileContent[i]);
                    i++;
                }

                int lineEnd = currentLine;
                string source = captured.ToString().Trim();

                if (!isExpression && source.StartsWith('='))
                {
                    isExpression = true;
                }

                if (source.Length > 0)
                {
                    blocks.Add(new VbscriptBlock(lineStart, lineEnd, source, isExpression));
                }
            }
            else
            {
                if (c == '\n')
                {
                    currentLine++;
                }

                i++;
            }
        }

        return blocks;
    }
}
