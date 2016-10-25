using System;
using System.Data.Common;
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
    public class AnyQueryHandler : IQueryHandler<bool>
    {
        private readonly QueryModel _query;
        private readonly IDocumentSchema _schema;

        public AnyQueryHandler(QueryModel query, IDocumentSchema schema)
        {
            _query = query;
            _schema = schema;
        }

        public Type SourceType => _query.SourceType();

        public void ConfigureCommand(NpgsqlCommand command)
        {
            var mapping = _schema.MappingFor(_query).ToQueryableDocument();

            var select = "select (count(*) > 0) as result";

            if (_query.HasSelectMany())
            {
                var selectMany = new SelectManyQuery(mapping, _query, 0);

                select = $"select (sum(jsonb_array_length({selectMany.SqlLocator})) > 0) as result";
            }

            var sql = $"{select} from {mapping.Table.QualifiedName} as d";

            sql = new LinqQuery<bool>(_schema, _query, new IIncludeJoin[0], null).AppendWhere(command, sql);

            command.AppendQuery(sql);
        }

        public bool Handle(DbDataReader reader, IIdentityMap map)
        {
            if (!reader.Read())
            {
                return false;
            }

            return !reader.IsDBNull(0) && reader.GetBoolean(0);
        }

        public async Task<bool> HandleAsync(DbDataReader reader, IIdentityMap map, CancellationToken token)
        {
            var hasRow = await reader.ReadAsync(token).ConfigureAwait(false);


            return hasRow && !(await reader.IsDBNullAsync(0, token).ConfigureAwait(false)) && await reader.GetFieldValueAsync<bool>(0, token).ConfigureAwait(false);
        }
    }
}