using System;
using Marten.Util;
using Npgsql;

namespace Marten.Linq
{
    internal interface IDbParameterSetter
    {
        void SetParameter(object query, NpgsqlParameter parameter);
        NpgsqlParameter AddParameter(object query, NpgsqlCommand command);
    }

    internal class DbParameterSetter<TObject, TProperty> : IDbParameterSetter
    {
        private readonly Func<TObject, TProperty> _getter;

        public DbParameterSetter(Func<TObject, TProperty> getter)
        {
            _getter = getter;
        }

        public void SetParameter(object query, NpgsqlParameter parameter)
        {
            var newValue = _getter((TObject) query);
            parameter.Value = newValue;
        }

        public NpgsqlParameter AddParameter(object query, NpgsqlCommand command)
        {
            var newValue = _getter((TObject)query);
            var param = command.AddParameter(newValue);

            return param;
        }
    }
}