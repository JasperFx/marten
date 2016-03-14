using System;
using Npgsql;

namespace Marten.Linq
{
    internal interface IDbParameterSetter
    {
        void SetParameter(object query, NpgsqlParameter parameter);
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
    }
}