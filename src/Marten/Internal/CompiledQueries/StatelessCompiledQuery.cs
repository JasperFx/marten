using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Linq.QueryHandlers;
using Marten.Util;

namespace Marten.Internal.CompiledQueries
{
    public abstract class StatelessCompiledQuery<TOut, TQuery> : IQueryHandler<TOut>
    {
        private IQueryHandler<TOut> _inner;
        protected readonly TQuery _query;
        protected readonly HardCodedParameters _hardcoded;

        public StatelessCompiledQuery(IQueryHandler<TOut> inner, TQuery query, HardCodedParameters hardcoded)
        {
            _inner = inner;
            _query = query;
            _hardcoded = hardcoded;
        }

        public abstract void ConfigureCommand(CommandBuilder builder, IMartenSession session);

        public TOut Handle(DbDataReader reader, IMartenSession session)
        {
            return _inner.Handle(reader, session);
        }

        public Task<TOut> HandleAsync(DbDataReader reader, IMartenSession session, CancellationToken token)
        {
            return _inner.HandleAsync(reader, session, token);
        }
    }
}
