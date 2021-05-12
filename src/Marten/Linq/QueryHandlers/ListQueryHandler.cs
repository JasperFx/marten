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
    internal class ListQueryHandler<T> : IQueryHandler<IReadOnlyList<T>>, IQueryHandler<IEnumerable<T>>, IMaybeStatefulHandler
    {
        private readonly Statement _statement;

        public ListQueryHandler(Statement statement, ISelector<T> selector)
        {
            _statement = statement;
            Selector = selector;
        }

        public ISelector<T> Selector { get; }

        public void ConfigureCommand(CommandBuilder builder, IMartenSession session)
        {
            _statement.Configure(builder);
        }

        public IReadOnlyList<T> Handle(DbDataReader reader, IMartenSession session)
        {
            var list = new List<T>();

            while (reader.Read())
            {
                var item = Selector.Resolve(reader);
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

            while (await reader.ReadAsync(token))
            {
                var item = await Selector.ResolveAsync(reader, token);
                list.Add(item);
            }

            return list;
        }

        public bool DependsOnDocumentSelector()
        {
            // There will be from dynamic codegen
            // ReSharper disable once SuspiciousTypeConversion.Global
            return Selector is IDocumentSelector;
        }

        public IQueryHandler CloneForSession(IMartenSession session, QueryStatistics statistics)
        {
            var selector = (ISelector<T>)session.StorageFor<T>().BuildSelector(session);

            return new ListQueryHandler<T>(null, selector);
        }
    }

}
