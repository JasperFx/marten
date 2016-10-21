using System;
using System.Data.Common;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Schema;
using Marten.Services;
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
            var mapping = _schema.MappingFor(_query.SourceType()).ToQueryableDocument();

            var select = "select count(*) as number";

            if (_query.HasSelectMany())
            {
                if (_query.HasOperator<DistinctResultOperator>())
                {
                    throw new NotSupportedException("Marten does not yet support SelectMany() with both a Distinct() and Count() operator");
                }

                var selectMany = _query.ToSelectManyQuery(mapping);

                select = $"select sum(jsonb_array_length({selectMany.SqlLocator})) as number";
            }

            var sql = $"{select} from {mapping.Table.QualifiedName} as d";

            var where = _schema.BuildWhereFragment(mapping, _query);

            sql = sql.AppendWhere(@where, command);

            command.AppendQuery(sql);
        }

        public T Handle(DbDataReader reader, IIdentityMap map)
        {
            var hasNext = reader.Read();
            return hasNext ? reader.GetFieldValue<T>(0) : default(T);
        }

        public async Task<T> HandleAsync(DbDataReader reader, IIdentityMap map, CancellationToken token)
        {
            var hasNext = await reader.ReadAsync().ConfigureAwait(false);
            return hasNext ? await reader.GetFieldValueAsync<T>(0, token).ConfigureAwait(false) : default(T);
        }
    }
}