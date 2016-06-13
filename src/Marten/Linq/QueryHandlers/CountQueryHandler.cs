using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Schema;
using Marten.Services;
using Marten.Util;
using Npgsql;
using Remotion.Linq;

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

            var sql = "select count(*) as number from " + mapping.Table.QualifiedName + " as d";

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
            var hasNext = await reader.ReadAsync(token).ConfigureAwait(false);
            return !token.IsCancellationRequested && hasNext
                    ? await reader.GetFieldValueAsync<T>(0, token).ConfigureAwait(false) 
                    : default(T);
        }
    }
}