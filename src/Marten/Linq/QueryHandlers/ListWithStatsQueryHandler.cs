using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Internal;
using Marten.Internal.CodeGeneration;
using Marten.Linq.Selectors;
using Marten.Linq.SqlGeneration;
using Weasel.Postgresql;
using Marten.Util;

namespace Marten.Linq.QueryHandlers
{
    internal class ListWithStatsQueryHandler<T>: IQueryHandler<IReadOnlyList<T>>, IQueryHandler<IEnumerable<T>>, IMaybeStatefulHandler
    {
        private readonly int _countIndex;
        private readonly Statement _statement;
        private readonly ISelector<T> _selector;
        private readonly QueryStatistics _statistics;

        public ListWithStatsQueryHandler(int countIndex, Statement statement, ISelector<T> selector, QueryStatistics statistics)
        {
            _countIndex = countIndex;
            _statement = statement;
            _selector = selector;
            _statistics = statistics;
        }

        public void ConfigureCommand(CommandBuilder builder, IMartenSession session)
        {
            _statement.Configure(builder);
        }

        public IReadOnlyList<T> Handle(DbDataReader reader, IMartenSession session)
        {
            var list = new List<T>();

            if (reader.Read())
            {
                _statistics.TotalResults = reader.GetFieldValue<int>(_countIndex);
                var item = _selector.Resolve(reader);
                list.Add(item);
            }
            else
            {
                // no data
                _statistics.TotalResults = 0;
                return list;
            }

            // Get the rest of the data
            while (reader.Read())
            {
                var item = _selector.Resolve(reader);
                list.Add(item);
            }

            return list;
        }

        async Task<IEnumerable<T>> IQueryHandler<IEnumerable<T>>.HandleAsync(DbDataReader reader, IMartenSession session, CancellationToken token)
        {
            var list = await HandleAsync(reader, session, token);
            return list;
        }

        IEnumerable<T> IQueryHandler<IEnumerable<T>>.Handle(DbDataReader reader, IMartenSession session)
        {
            return Handle(reader, session);
        }

        public async Task<IReadOnlyList<T>> HandleAsync(DbDataReader reader, IMartenSession session, CancellationToken token)
        {
            var list = new List<T>();

            if (await reader.ReadAsync(token))
            {
                _statistics.TotalResults = await reader.GetFieldValueAsync<int>(_countIndex, token);
                var item = await _selector.ResolveAsync(reader, token);
                list.Add(item);
            }
            else
            {
                // no data
                _statistics.TotalResults = 0;
                return list;
            }

            // Get the rest of the data
            while (await reader.ReadAsync(token))
            {
                var item = await _selector.ResolveAsync(reader, token);
                list.Add(item);
            }

            return list;
        }

        public bool DependsOnDocumentSelector()
        {
            // There will be from dynamic codegen
            // ReSharper disable once SuspiciousTypeConversion.Global
            return _selector is IDocumentSelector;
        }

        public IQueryHandler CloneForSession(IMartenSession session, QueryStatistics statistics)
        {
            var selector = (ISelector<T>)session.StorageFor<T>().BuildSelector(session);

            return new ListWithStatsQueryHandler<T>(_countIndex, null, selector, statistics);
        }
    }
}
