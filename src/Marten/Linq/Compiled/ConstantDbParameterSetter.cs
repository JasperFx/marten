using Marten.Util;
using Npgsql;

namespace Marten.Linq.Compiled
{
    internal class ConstantDbParameterSetter : IDbParameterSetter
    {
        private readonly object _value;

        public ConstantDbParameterSetter(object value)
        {
            _value = value;
        }

        public NpgsqlParameter AddParameter(object query, CommandBuilder command)
        {
            var param = command.AddParameter(_value);

            return param;
        }
    }
}