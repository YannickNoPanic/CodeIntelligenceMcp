using VBScript.Parser.Ast.Expressions;

namespace VBScript.Parser.Ast.Statements
{
    public class DoStatement(LoopType loopType, ConditionTestType testType, Expression condition) : Statement
    {
        public LoopType LoopType { get; } = loopType;
        public ConditionTestType TestType { get; } = testType;
        public Expression Condition { get; } = condition;
        public List<Statement> Body { get; } = [];
    }

    public enum ConditionTestType
    {
        None,
        PreTest,
        PostTest,
    }

    public enum LoopType
    {
        None,
        While,
        Until,
    }
}
