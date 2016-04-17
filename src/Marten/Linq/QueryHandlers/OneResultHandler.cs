using System;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Schema;
using Marten.Services;
using Marten.Services.Includes;
using Marten.Util;
using Npgsql;
using Remotion.Linq;

namespace Marten.Linq.QueryHandlers
{
    public class OneResultHandler<T> : IQueryHandler<T>
    {
        private const string NoElementsMessage = "Sequence contains no elements";
        private const string MoreThanOneElementMessage = "Sequence contains more than one element";
        private readonly int _rowLimit;
        private readonly IDocumentMapping _mapping;
        private readonly IDocumentSchema _schema;
        private readonly QueryModel _query;
        private readonly bool _canBeNull;
        private readonly bool _canBeMultiples;
        private readonly ISelector<T> _selector;

        public static IQueryHandler<T> Single(IDocumentSchema schema, QueryModel query, IIncludeJoin[] joins)
        {
            return new OneResultHandler<T>(2, schema, query, joins, canBeNull:false, canBeMultiples:false);
        }

        public static IQueryHandler<T> SingleOrDefault(IDocumentSchema schema, QueryModel query, IIncludeJoin[] joins)
        {
            return new OneResultHandler<T>(2, schema, query, joins, canBeNull: true, canBeMultiples: false);
        }

        public static IQueryHandler<T> First(IDocumentSchema schema, QueryModel query, IIncludeJoin[] joins)
        {
            return new OneResultHandler<T>(1, schema, query, joins, canBeNull: false, canBeMultiples: true);
        }

        public static IQueryHandler<T> FirstOrDefault(IDocumentSchema schema, QueryModel query, IIncludeJoin[] joins)
        {
            return new OneResultHandler<T>(1, schema, query, joins, canBeNull: true, canBeMultiples: true);
        }

        public OneResultHandler(int rowLimit, IDocumentSchema schema, QueryModel query, IIncludeJoin[] joins, bool canBeNull = true, bool canBeMultiples = true)
        {
            _rowLimit = rowLimit;
            _mapping = schema.MappingFor(query);
            _schema = schema;
            _query = query;
            _canBeNull = canBeNull;
            _canBeMultiples = canBeMultiples;

            var selector = _schema.BuildSelector<T>(_mapping, _query);

            if (joins.Any())
            {
                selector = new IncludeSelector<T>(schema, selector, joins);
            }

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

            sql += $" LIMIT {_rowLimit}";
            sql = _query.AppendOffset(sql);

            command.AppendQuery(sql);
        }

        public T Handle(DbDataReader reader, IIdentityMap map)
        {
            var hasResult = reader.Read();
            if (!hasResult)
            {
                if (_canBeNull) return default(T);

                throw new InvalidOperationException(NoElementsMessage);
            }
            
            var result = _selector.Resolve(reader, map);

            if (!_canBeMultiples && reader.Read())
            {
                throw new InvalidOperationException(MoreThanOneElementMessage);
            }

            return result;
        }

        public async Task<T> HandleAsync(DbDataReader reader, IIdentityMap map, CancellationToken token)
        {
            var hasResult = await reader.ReadAsync(token);
            if (!hasResult)
            {
                if (_canBeNull) return default(T);

                throw new InvalidOperationException(NoElementsMessage);
            }

            var result = await _selector.ResolveAsync(reader, map, token).ConfigureAwait(false);

            if (!_canBeMultiples && await reader.ReadAsync(token).ConfigureAwait(false))
            {
                throw new InvalidOperationException(MoreThanOneElementMessage);
            }

            return result;
        }
    }
}