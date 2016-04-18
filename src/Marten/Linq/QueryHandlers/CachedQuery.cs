using System.Collections.Generic;
using System.Linq;
using Baseline;
using Npgsql;

namespace Marten.Linq.QueryHandlers
{
    internal class CachedQuery
    {
        public object Handler { get; set; }

        public IList<IDbParameterSetter> ParameterSetters { get; set; }

        public NpgsqlCommand Command { get; set; }

        public IQueryHandler<T> CreateHandler<T>(object model)
        {
            return new CachedQueryHandler<T>(model, Command, Handler.As<IQueryHandler<T>>(), ParameterSetters.ToArray());
        }
    }
}