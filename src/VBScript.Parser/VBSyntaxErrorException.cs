namespace VBScript.Parser
{
    public class VBSyntaxErrorException(VBSyntaxErrorCode code, int line, int position, string message, Exception? innerException) : Exception(message, innerException)
    {
        public VBSyntaxErrorException(VBSyntaxErrorCode code, int line, int position)
            : this(code, line, position, VBSyntaxErrorMessages.ResourceManager.GetString(code))
        {
        }

        public VBSyntaxErrorException(VBSyntaxErrorCode code, int line, int position, string message)
            : this(code, line, position, message, null)
        {
        }

        public VBSyntaxErrorCode Code { get; } = code;
        public int Line { get; } = line;
        public int Position { get; } = position;
    }
}
