using LamarCodeGeneration;

namespace Marten.Events.V4Concept.CodeGeneration
{
    // TODO -- move this back to Lamar itself
    internal class IfStyle
    {
        private readonly bool _writes;
        internal static readonly IfStyle If = new IfStyle("if");
        internal static readonly IfStyle ElseIf = new IfStyle("else if");
        internal static readonly IfStyle Else = new IfStyle("else");
        internal static readonly IfStyle None = new IfStyle("else", false);

        public string Code { get; }

        private IfStyle(string code, bool writes = true)
        {
            _writes = writes;
            Code = code;
        }

        public void Open(ISourceWriter writer, string condition)
        {
            if (_writes)
            {
                writer.Write(condition.IsEmpty()
                    ? $"BLOCK:{Code}"
                    : $"BLOCK:{Code} ({condition})");
            }
        }

        public void Close(ISourceWriter writer)
        {
            if (_writes)
            {
                writer.FinishBlock();
            }
        }

        public override string ToString()
        {
            return Code;
        }
    }
}
