using VBScript.Parser.Ast.Expressions;

namespace VBScript.Parser.Ast
{
    public class Parameter(Identifier id, ParameterModifier modifier, bool parentheses) : Node
    {
        public ParameterModifier Modifier { get; } = modifier;
        public bool Parentheses { get; } = parentheses;
        public Identifier Identifier { get; } = id ?? throw new ArgumentNullException(nameof(id));
    }

    public enum ParameterModifier
    {
        None, ByRef, ByVal
    }
}
