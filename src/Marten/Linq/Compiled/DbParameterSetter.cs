using System;
using Marten.Util;
using Npgsql;

namespace Marten.Linq.Compiled
{
    internal class DbParameterSetter<TObject, TProperty> : IDbParameterSetter
    {
        private readonly Func<TObject, TProperty> _getter;

        public DbParameterSetter(Func<TObject, TProperty> getter)
        {
            if (typeof(TProperty) == typeof(QueryStatistics))
            {
                throw new ArgumentOutOfRangeException($"Use {nameof(QueryStatisticsFinder<TObject>)} for QueryStatistics properties");
            }

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