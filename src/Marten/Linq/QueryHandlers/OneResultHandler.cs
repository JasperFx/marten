using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Linq.Model;
using Marten.Services;
using Npgsql;

namespace Marten.Linq.QueryHandlers
{
    public class OneResultHandler<T> : IQueryHandler<T>
    {
        private const string NoElementsMessage = "Sequence contains no elements";
        private const string MoreThanOneElementMessage = "Sequence contains more than one element";
        private readonly bool _canBeMultiples;
        private readonly bool _canBeNull;
        private readonly LinqQuery<T> _linqQuery;
        private readonly int _rowLimit;

        public OneResultHandler(int rowLimit, LinqQuery<T> linqQuery,
            bool canBeNull = true, bool canBeMultiples = true)
        {
            _rowLimit = rowLimit;
            _linqQuery = linqQuery;
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
                throw new InvalidOperationException(MoreThanOneElementMessage);

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
                throw new InvalidOperationException(MoreThanOneElementMessage);

            return result;
        }

        public static IQueryHandler<T> Single(LinqQuery<T> query)
        {
            return new OneResultHandler<T>(2, query, false, false);
        }

        public static IQueryHandler<T> SingleOrDefault(LinqQuery<T> query)
        {
            return new OneResultHandler<T>(2, query, true, false);
        }

        public static IQueryHandler<T> First(LinqQuery<T> query)
        {
            return new OneResultHandler<T>(1, query, false, true);
        }

        public static IQueryHandler<T> FirstOrDefault(LinqQuery<T> query)
        {
            return new OneResultHandler<T>(1, query, true, true);
        }
    }
}