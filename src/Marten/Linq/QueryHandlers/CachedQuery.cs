using System.Collections.Generic;
using System.Linq;
using Baseline;
using Marten.Linq.Compiled;
using Npgsql;

namespace Marten.Linq.QueryHandlers
{
    internal class CachedQuery
    {
        public object Handler { get; set; }

        public IList<IDbParameterSetter> ParameterSetters { get; set; }

        public NpgsqlCommand Command { get; set; }

        public IQueryStatisticsFinder StatisticsFinder { get; set; }

        public IQueryHandler<T> CreateHandler<T>(object model, ISerializer serializer, out QueryStatistics stats)
        {
            stats = StatisticsFinder?.Find(model);

            return new CachedQueryHandler<T>(model, Command, Handler.As<IQueryHandler<T>>(), ParameterSetters.ToArray(), serializer);
        }
    }
}