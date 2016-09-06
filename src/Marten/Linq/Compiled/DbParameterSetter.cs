using System;
using Marten.Util;
using Npgsql;

namespace Marten.Linq
{
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
}