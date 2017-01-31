using Npgsql;

namespace Marten.Linq
{
    public interface IWhereFragment
    {
        // TODO -- have this take in a StringBuilder? Have to be in 2.0
        string ToSql(NpgsqlCommand command);
        bool Contains(string sqlText);
    }
}