using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Linq;
using Marten.Util;

namespace Marten.V4Internals.Linq.QueryHandlers
{
    public class ListQueryHandler<T>: IQueryHandler<IReadOnlyList<T>>, IQueryHandler<IEnumerable<T>>
    {
        private readonly Statement _statement;
        private readonly ISelector<T> _selector;

        public ListQueryHandler(Statement statement, ISelector<T> selector)
        {
            _statement = statement;
            _selector = selector;
        }

        public void ConfigureCommand(CommandBuilder builder, IMartenSession session)
        {
            // TODO -- how are statistics going to work this time?
            _statement.Configure(builder, false);
        }

        public IReadOnlyList<T> Handle(DbDataReader reader, IMartenSession session, QueryStatistics stats)
        {
            var list = new List<T>();

            while (reader.Read())
            {
                var item = _selector.Resolve(reader);
                list.Add(item);
            }

            return list;
        }

        async Task<IEnumerable<T>> IQueryHandler<IEnumerable<T>>.HandleAsync(DbDataReader reader, IMartenSession session, QueryStatistics stats, CancellationToken token)
        {
            var list = await HandleAsync(reader, session, stats, token).ConfigureAwait(false);
            return list;
        }

        IEnumerable<T> IQueryHandler<IEnumerable<T>>.Handle(DbDataReader reader, IMartenSession session, QueryStatistics stats)
        {
            return Handle(reader, session, stats);
        }

        public async Task<IReadOnlyList<T>> HandleAsync(DbDataReader reader, IMartenSession session, QueryStatistics stats, CancellationToken token)
        {
            var list = new List<T>();

            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                var item = await _selector.ResolveAsync(reader, token).ConfigureAwait(false);
                list.Add(item);
            }

            return list;
        }
    }

}
