using System.Text;
using System.Text.RegularExpressions;
using CodeIntelligenceMcp.JavaScript.Models;

namespace CodeIntelligenceMcp.JavaScript;

/// <summary>
/// Extracts blocks from Vue Single File Components (.vue files).
/// Character-by-character scan with line tracking, modeled on AspBlockExtractor.
/// </summary>
public static class VueSfcExtractor
{
    private static readonly Regex LangAttr =
        new(@"lang=['""]([^'""]+)['""]", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex DefineProps =
        new(@"defineProps\s*(?:<[^>]*>)?\s*\(\s*\{([^}]*)\}", RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex DefinePropsArray =
        new(@"defineProps\s*\(\s*\[([^\]]+)\]", RegexOptions.Compiled);

    private static readonly Regex DefineEmits =
        new(@"defineEmits\s*\(\s*\[([^\]]+)\]", RegexOptions.Compiled);

    private static readonly Regex ComposableCall =
        new(@"\buse[A-Z]\w*\s*\(", RegexOptions.Compiled);

    private static readonly string[] SfcTags = ["template", "script", "style"];

    public static VueSfcInfo Extract(string filePath, string content)
    {
        var blocks = ExtractBlocks(content);
        VueSfcBlock? scriptBlock = blocks.FirstOrDefault(b =>
            b.Tag == "script" && b.IsSetup) ??
            blocks.FirstOrDefault(b => b.Tag == "script");

        JsFileInfo? scriptAnalysis = null;
        List<string> props = [];
        List<string> emits = [];
        List<string> composables = [];

        if (scriptBlock is not null)
        {
            scriptAnalysis = JsFileParser.Parse(filePath, scriptBlock.Content, scriptBlock.LineStart - 1);
            props = ExtractProps(scriptBlock.Content);
            emits = ExtractEmits(scriptBlock.Content);
            composables = ExtractComposables(scriptBlock.Content);
        }

        return new VueSfcInfo(filePath, blocks, scriptAnalysis, props, emits, composables);
    }

    private static IReadOnlyList<VueSfcBlock> ExtractBlocks(string content)
    {
        var blocks = new List<VueSfcBlock>();
        int length = content.Length;
        int currentLine = 1;
        int i = 0;

        while (i < length)
        {
            // Look for < at top level
            if (content[i] != '<')
            {
                if (content[i] == '\n') currentLine++;
                i++;
                continue;
            }

            // Try to match one of our SFC tags
            string? matchedTag = null;
            foreach (string tag in SfcTags)
            {
                if (i + tag.Length + 1 < length &&
                    content.AsSpan(i + 1, tag.Length).Equals(tag, StringComparison.OrdinalIgnoreCase) &&
                    (content[i + 1 + tag.Length] is ' ' or '\t' or '\r' or '\n' or '>' or '/'))
                {
                    matchedTag = tag;
                    break;
                }
            }

            if (matchedTag is null)
            {
                if (content[i] == '\n') currentLine++;
                i++;
                continue;
            }

            int blockLineStart = currentLine;

            // Read the opening tag to extract attributes
            int tagEnd = content.IndexOf('>', i);
            if (tagEnd < 0) break;

            string openingTag = content[i..(tagEnd + 1)];
            bool isSelfClosing = openingTag.TrimEnd().EndsWith("/>", StringComparison.Ordinal);
            if (isSelfClosing)
            {
                // Self-closing — no content
                for (int j = i; j <= tagEnd; j++)
                    if (content[j] == '\n') currentLine++;
                i = tagEnd + 1;
                continue;
            }

            string? lang = ExtractLang(openingTag);
            bool isSetup = openingTag.Contains("setup", StringComparison.OrdinalIgnoreCase);

            // Advance past the opening tag
            for (int j = i; j <= tagEnd; j++)
                if (content[j] == '\n') currentLine++;
            i = tagEnd + 1;

            int contentStart = i;
            int contentLineStart = currentLine;

            // Scan for the closing tag </matchedTag>
            string closingTag = $"</{matchedTag}";
            int closeIdx = -1;
            int j2 = i;
            while (j2 < length)
            {
                if (content[j2] == '\n') currentLine++;

                if (content[j2] == '<' &&
                    j2 + closingTag.Length <= length &&
                    content.AsSpan(j2, closingTag.Length).Equals(closingTag, StringComparison.OrdinalIgnoreCase))
                {
                    closeIdx = j2;
                    break;
                }
                j2++;
            }

            if (closeIdx < 0) break;

            string blockContent = content[contentStart..closeIdx].Trim('\r', '\n');
            int blockLineEnd = currentLine;

            blocks.Add(new VueSfcBlock(matchedTag, lang, isSetup, blockLineStart, blockLineEnd, blockContent));

            // Advance past closing tag
            int closingEnd = content.IndexOf('>', closeIdx);
            if (closingEnd >= 0)
            {
                for (int j = closeIdx; j <= closingEnd; j++)
                    if (content[j] == '\n') currentLine++;
                i = closingEnd + 1;
            }
            else
            {
                i = closeIdx + closingTag.Length;
            }
        }

        return blocks;
    }

    private static string? ExtractLang(string openingTag)
    {
        Match m = LangAttr.Match(openingTag);
        return m.Success ? m.Groups[1].Value : null;
    }

    private static List<string> ExtractProps(string scriptContent)
    {
        var result = new List<string>();

        // Object syntax: defineProps({ propName: ... })
        Match objectMatch = DefineProps.Match(scriptContent);
        if (objectMatch.Success)
        {
            string propsBody = objectMatch.Groups[1].Value;
            foreach (Match propLine in Regex.Matches(propsBody, @"(\w+)\s*:"))
                result.Add(propLine.Groups[1].Value);
            return result;
        }

        // Array syntax: defineProps(['propName', 'other'])
        Match arrayMatch = DefinePropsArray.Match(scriptContent);
        if (arrayMatch.Success)
        {
            foreach (Match name in Regex.Matches(arrayMatch.Groups[1].Value, @"['""](\w+)['""]"))
                result.Add(name.Groups[1].Value);
        }

        return result;
    }

    private static List<string> ExtractEmits(string scriptContent)
    {
        var result = new List<string>();
        Match m = DefineEmits.Match(scriptContent);
        if (m.Success)
        {
            foreach (Match name in Regex.Matches(m.Groups[1].Value, @"['""]([^'""]+)['""]"))
                result.Add(name.Groups[1].Value);
        }
        return result;
    }

    private static List<string> ExtractComposables(string scriptContent)
    {
        return ComposableCall.Matches(scriptContent)
            .Select(m => m.Value.TrimEnd('(', ' '))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(c => c)
            .ToList();
    }
}
