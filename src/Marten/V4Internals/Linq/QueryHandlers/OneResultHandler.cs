using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Linq;
using Marten.Util;

namespace Marten.V4Internals.Linq.QueryHandlers
{
    public class OneResultHandler<T>: IQueryHandler<T>
    {
        private const string NoElementsMessage = "Sequence contains no elements";
        private const string MoreThanOneElementMessage = "Sequence contains more than one element";
        private readonly bool _canBeMultiples;
        private readonly bool _canBeNull;
        private readonly ISelector<T> _selector;
        private readonly Statement _statement;

        public OneResultHandler(Statement statement, ISelector<T> selector,
            bool canBeNull = true, bool canBeMultiples = true)
        {
            _statement = statement;
            _selector = selector;
            _canBeNull = canBeNull;
            _canBeMultiples = canBeMultiples;
        }

        public void ConfigureCommand(CommandBuilder builder, IMartenSession session)
        {
            _statement.Configure(builder, false);
        }

        public T Handle(DbDataReader reader, IMartenSession session, QueryStatistics stats)
        {
            var hasResult = reader.Read();
            if (!hasResult)
            {
                if (_canBeNull)
                    return default;

                throw new InvalidOperationException(NoElementsMessage);
            }

            var result = _selector.Resolve(reader);

            if (!_canBeMultiples && reader.Read())
                throw new InvalidOperationException(MoreThanOneElementMessage);

            return result;
        }

        public async Task<T> HandleAsync(DbDataReader reader, IMartenSession session, QueryStatistics stats,
            CancellationToken token)
        {
            var hasResult = await reader.ReadAsync(token).ConfigureAwait(false);
            if (!hasResult)
            {
                if (_canBeNull)
                    return default;

                throw new InvalidOperationException(NoElementsMessage);
            }

            var result = await _selector.ResolveAsync(reader, token).ConfigureAwait(false);

            if (!_canBeMultiples && await reader.ReadAsync(token).ConfigureAwait(false))
                throw new InvalidOperationException(MoreThanOneElementMessage);

            return result;
        }
    }
}
