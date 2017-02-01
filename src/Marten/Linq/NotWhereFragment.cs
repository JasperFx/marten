using Marten.Util;
using Npgsql;

namespace Marten.Linq
{
    public class NotWhereFragment : IWhereFragment
    {
        private readonly IWhereFragment _inner;

        public NotWhereFragment(IWhereFragment inner)
        {
            _inner = inner;
            
        }

        public string ToSql(CommandBuilder command)
        {
            return $"NOT({_inner.ToSql(command)})";
        }

        public bool Contains(string sqlText)
        {
            return "NOT".Contains(sqlText) || _inner.Contains(sqlText);
        }
    }
}