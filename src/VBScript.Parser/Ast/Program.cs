using VBScript.Parser.Ast.Statements;

namespace VBScript.Parser.Ast
{
    public class Program(bool optionExplicit) : Node
    {
        public bool OptionExplicit { get; } = optionExplicit;
        public List<Statement> Body { get; } = [];
        public List<Comment> Comments { get; } = [];
    }
}
