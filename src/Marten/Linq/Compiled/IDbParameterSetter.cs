using Npgsql;

namespace Marten.Linq.Compiled
{
    internal interface IDbParameterSetter
    {
        NpgsqlParameter AddParameter(object query, NpgsqlCommand command);
    }
}