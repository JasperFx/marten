using Npgsql;

namespace Marten.Linq
{
    public interface IWhereFragment
    {
        string ToSql(NpgsqlCommand command);
    }
}