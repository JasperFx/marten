using Npgsql;

namespace Marten.Linq
{
    internal interface IDbParameterSetter
    {
        NpgsqlParameter AddParameter(object query, NpgsqlCommand command);
    }
}