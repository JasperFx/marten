using System;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Linq.Model;
using Marten.Schema;
using Marten.Services;
using Marten.Services.Includes;
using Npgsql;
using Remotion.Linq;

namespace Marten.Linq.QueryHandlers
{
    public class OneResultHandler<T> : IQueryHandler<T>
    {
        private const string NoElementsMessage = "Sequence contains no elements";
        private const string MoreThanOneElementMessage = "Sequence contains more than one element";
        private readonly int _rowLimit;
        private readonly bool _canBeNull;
        private readonly bool _canBeMultiples;
        private readonly LinqQuery<T> _linqQuery;

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

        [Obsolete("Just take in LinqQuery instead.")]
        public OneResultHandler(int rowLimit, IDocumentSchema schema, QueryModel query, IIncludeJoin[] joins, bool canBeNull = true, bool canBeMultiples = true)
        {
            _linqQuery = new LinqQuery<T>(schema, query, joins, null);
            _rowLimit = rowLimit;
            _canBeNull = canBeNull;
            _canBeMultiples = canBeMultiples;
        }

        public Type SourceType => _linqQuery.SourceType;
        public void ConfigureCommand(NpgsqlCommand command)
        {
            _linqQuery.ConfigureCommand(command, _rowLimit);
        }

        public T Handle(DbDataReader reader, IIdentityMap map)
        {
            var hasResult = reader.Read();
            if (!hasResult)
            {
                if (_canBeNull) return default(T);

                throw new InvalidOperationException(NoElementsMessage);
            }
            
            var result = _linqQuery.Selector.Resolve(reader, map);

            if (!_canBeMultiples && reader.Read())
            {
                throw new InvalidOperationException(MoreThanOneElementMessage);
            }

            return result;
        }

        public async Task<T> HandleAsync(DbDataReader reader, IIdentityMap map, CancellationToken token)
        {
            var hasResult = await reader.ReadAsync(token).ConfigureAwait(false);
            if (!hasResult)
            {
                if (_canBeNull) return default(T);

                throw new InvalidOperationException(NoElementsMessage);
            }

            var result = await _linqQuery.Selector.ResolveAsync(reader, map, token).ConfigureAwait(false);

            if (!_canBeMultiples && await reader.ReadAsync(token).ConfigureAwait(false))
            {
                throw new InvalidOperationException(MoreThanOneElementMessage);
            }

            return result;
        }
    }
}