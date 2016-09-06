using Marten.Util;
using Npgsql;

namespace Marten.Linq
{
    internal class ConstantDbParameterSetter : IDbParameterSetter
    {
        private readonly object _value;

        public ConstantDbParameterSetter(object value)
        {
            _value = value;
        }

        public NpgsqlParameter AddParameter(object query, NpgsqlCommand command)
        {
            var param = command.AddParameter(_value);

            return param;
        }
    }
}