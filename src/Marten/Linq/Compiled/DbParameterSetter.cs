using System;
using Baseline;
using Marten.Linq.Parsing;
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

        public StringComparisonParser Parser { get; set; }

        public NpgsqlParameter AddParameter(object query, NpgsqlCommand command)
        {
            var newValue = _getter((TObject)query);


            if (Parser != null)
            {
                newValue = Parser.FormatValue(null, newValue.ToString()).As<TProperty>();
            }

            var param = command.AddParameter(newValue);

            return param;
        }
    }
}