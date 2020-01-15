using Marten.Util;

namespace Marten.Linq
{
    public class NotWhereFragment: IWhereFragment
    {
        private readonly IWhereFragment _inner;

        public NotWhereFragment(IWhereFragment inner)
        {
            _inner = inner;
        }

        public void Apply(CommandBuilder builder)
        {
            builder.Append("NOT(");
            _inner.Apply(builder);
            builder.Append(")");
        }

        public bool Contains(string sqlText)
        {
            return "NOT".Contains(sqlText) || _inner.Contains(sqlText);
        }
    }
}
