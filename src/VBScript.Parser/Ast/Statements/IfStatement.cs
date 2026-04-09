using VBScript.Parser.Ast.Expressions;

namespace VBScript.Parser.Ast.Statements
{
    public class IfStatement(Expression test, Statement consequent, Statement? alternate) : Statement
    {
        public Expression Test { get; } = test ?? throw new ArgumentNullException(nameof(test));
        public Statement Consequent { get; } = consequent ?? throw new ArgumentNullException(nameof(consequent));
        public Statement? Alternate { get; } = alternate;
    }
}
