using VBScript.Parser.Ast.Expressions;

namespace VBScript.Parser.Ast.Statements
{
    public class CaseStatement : Statement
    {
        public CaseStatement()
        {

        }

        public List<Expression> Values { get; } = [];
        public List<Statement> Body { get; } = [];
    }
}
