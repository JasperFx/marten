using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Linq.Model;
using Marten.Schema;
using Marten.Services;
using Marten.Services.Includes;
using Marten.Util;
using Npgsql;
using Remotion.Linq;

namespace Marten.Linq.QueryHandlers
{
    public class EnumerableQueryHandler<T> : IQueryHandler<IEnumerable<T>>
    {
        private readonly IQueryHandler<IList<T>> _inner;

        public EnumerableQueryHandler(IDocumentSchema schema, QueryModel query, IIncludeJoin[] joins, QueryStatistics stats)
        {
            _inner = new LinqQuery<T>(schema, query, joins, stats).ToList();
        }

        public Type SourceType => typeof (T);
        public void ConfigureCommand(CommandBuilder builder)
        {
            _inner.ConfigureCommand(builder);
        }

        public IEnumerable<T> Handle(DbDataReader reader, IIdentityMap map, QueryStatistics stats)
        {
            return _inner.Handle(reader, map, stats);
        }

        public async Task<IEnumerable<T>> HandleAsync(DbDataReader reader, IIdentityMap map, QueryStatistics stats, CancellationToken token)
        {
            return (await _inner.HandleAsync(reader, map, stats, token).ConfigureAwait(false));
        }
    }
}