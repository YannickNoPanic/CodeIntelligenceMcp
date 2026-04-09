using System.Diagnostics;

namespace VBScript.Parser.Ast
{
    [DebuggerDisplay("{Text}")]
    public class Comment(CommentType type, string text)
    {
        public CommentType Type { get; } = type;
        public string Text { get; } = text;
        public Range Range { get; set; }
        public Location Location { get; set; }
    }

    public enum CommentType
    {
        Rem,
        SingleQuote
    }
}
