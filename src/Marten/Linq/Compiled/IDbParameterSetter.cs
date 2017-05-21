using Marten.Util;
using Npgsql;

namespace Marten.Linq.Compiled
{
    public interface IDbParameterSetter
    {
        NpgsqlParameter AddParameter(object query, CommandBuilder command);
        void ReplaceValue(NpgsqlParameter cmdParameter);
    }
}