using Npgsql;

namespace Marten.Linq.Compiled
{
    public interface IDbParameterSetter
    {
        NpgsqlParameter AddParameter(object query, NpgsqlCommand command);
    }
}