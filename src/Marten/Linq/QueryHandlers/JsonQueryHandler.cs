using System;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Schema;
using Marten.Services;
using Marten.Util;
using Npgsql;
using Remotion.Linq;

namespace Marten.Linq.QueryHandlers
{
    public class JsonQueryHandler : IQueryHandler<string>
    {
        private readonly IDocumentSchema _schema;
        private readonly QueryModel _query;
        private readonly ISelector<string> _selector;
        private readonly IDocumentMapping _mapping;

        public JsonQueryHandler(IDocumentSchema schema, QueryModel query)
        {
            _mapping = schema.MappingFor(query);
            _schema = schema;
            _query = query;

            var selector = _schema.BuildSelector<string>(_mapping, _query);

            _selector = selector;
        }


        public Type SourceType => _query.SourceType();
        public void ConfigureCommand(NpgsqlCommand command)
        {
            var sql = _selector.ToSelectClause(_mapping);
            var @where = _schema.BuildWhereFragment(_mapping, _query);



            sql = sql.AppendWhere(@where, command);

            var orderBy = _query.ToOrderClause(_mapping);
            if (orderBy.IsNotEmpty()) sql += orderBy;

            sql = _query.AppendLimit(sql);
            sql = _query.AppendOffset(sql);

            command.AppendQuery(sql);
        }

        public string Handle(DbDataReader reader, IIdentityMap map)
        {
            return $"[{_selector.Read(reader, map).ToArray().Join(",")}]";
        }

        public async Task<string> HandleAsync(DbDataReader reader, IIdentityMap map, CancellationToken token)
        {
            return await _selector.ReadAsync(reader, map, token).ContinueWith(t=> $"[{t.Result.Join(",")}]", token).ConfigureAwait(false);
        }
    }
}