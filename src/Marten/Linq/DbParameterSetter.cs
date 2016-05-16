using System;
using Marten.Util;
using Npgsql;

namespace Marten.Linq
{
    internal interface IDbParameterSetter
    {
        NpgsqlParameter AddParameter(object query, NpgsqlCommand command);
    }

    internal class DbParameterSetter<TObject, TProperty> : IDbParameterSetter
    {
        private readonly Func<TObject, TProperty> _getter;

        public DbParameterSetter(Func<TObject, TProperty> getter)
        {
            _getter = getter;
        }

        public NpgsqlParameter AddParameter(object query, NpgsqlCommand command)
        {
            var newValue = _getter((TObject)query);
            var param = command.AddParameter(newValue);

            return param;
        }
    }


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