using System;
using System.Data.Common;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Linq.Model;
using Marten.Schema;
using Marten.Services;
using Marten.Services.Includes;
using Marten.Util;
using Npgsql;
using Remotion.Linq;
using Remotion.Linq.Clauses;
using Remotion.Linq.Clauses.Expressions;
using Remotion.Linq.Clauses.ResultOperators;

namespace Marten.Linq.QueryHandlers
{
    public class CountQueryHandler<T> : IQueryHandler<T>
    {
        private readonly QueryModel _query;
        private readonly IDocumentSchema _schema;

        public CountQueryHandler(QueryModel query, IDocumentSchema schema)
        {
            _query = query;
            _schema = schema;
        }

        public Type SourceType => _query.SourceType();

        public void ConfigureCommand(NpgsqlCommand command)
        {
            new LinqQuery<T>(_schema, _query, new IIncludeJoin[0], null)
                .ConfigureCount(command);
        }

        public T Handle(DbDataReader reader, IIdentityMap map)
        {
            var hasNext = reader.Read();
            return hasNext ? reader.GetFieldValue<T>(0) : default(T);
        }

        public async Task<T> HandleAsync(DbDataReader reader, IIdentityMap map, CancellationToken token)
        {
            var hasNext = await reader.ReadAsync(token).ConfigureAwait(false);
            return hasNext ? await reader.GetFieldValueAsync<T>(0, token).ConfigureAwait(false) : default(T);
        }
    }
}