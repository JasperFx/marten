using Marten.Util;
using Npgsql;

namespace Marten.Linq
{
    public interface IWhereFragment
    {
        // TODO -- have this take in a StringBuilder? Have to be in 2.0
        string ToSql(CommandBuilder command);
        bool Contains(string sqlText);
    }
}