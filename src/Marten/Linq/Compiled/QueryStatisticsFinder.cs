using System;
using System.Reflection;
using Marten.Util;

namespace Marten.Linq.Compiled
{
    internal class QueryStatisticsFinder<TObject>: IQueryStatisticsFinder
    {
        private readonly Func<TObject, QueryStatistics> _getter;
        private readonly Action<TObject, QueryStatistics> _setter;

        public QueryStatisticsFinder(PropertyInfo property)
        {
            _getter = LambdaBuilder.GetProperty<TObject, QueryStatistics>(property);
            if (property.CanWrite)
            {
                _setter = LambdaBuilder.SetProperty<TObject, QueryStatistics>(property);
            }
        }

        public QueryStatistics Find(object query)
        {
            var stats = _getter((TObject)query);
            if (stats == null)
            {
                stats = new QueryStatistics();
                _setter((TObject)query, stats);
            }

            return stats;
        }
    }
}
