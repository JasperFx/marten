using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Linq;
using Marten.Linq.QueryHandlers;
using Marten.Util;

namespace Marten.Internal.CompiledQueries
{
    public abstract class ClonedCompiledQuery<TOut, TQuery> : IQueryHandler<TOut>
    {
        private readonly IMaybeStatefulHandler _inner;
        private readonly QueryStatistics _statistics;

        public ClonedCompiledQuery(IMaybeStatefulHandler inner, QueryStatistics statistics)
        {
            _inner = inner;
            _statistics = statistics;
        }

        public abstract void ConfigureCommand(CommandBuilder builder, IMartenSession session);

        public TOut Handle(DbDataReader reader, IMartenSession session)
        {
            var inner = (IQueryHandler<TOut>)_inner.CloneForSession(session, _statistics);
            return inner.Handle(reader, session);
        }

        public Task<TOut> HandleAsync(DbDataReader reader, IMartenSession session, CancellationToken token)
        {
            var inner = (IQueryHandler<TOut>)_inner.CloneForSession(session, _statistics);
            return inner.HandleAsync(reader, session, token);
        }

        protected string StartsWith(string value)
        {
            return $"%{value}";
        }

        protected string ContainsString(string value)
        {
            return $"%{value}%";
        }

        protected string EndsWith(string value)
        {
            return $"{value}%";
        }
    }
}
