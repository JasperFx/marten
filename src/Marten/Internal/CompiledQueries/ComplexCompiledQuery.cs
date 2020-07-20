using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Internal.Linq;
using Marten.Util;

namespace Marten.Internal.CompiledQueries
{
    public abstract class ComplexCompiledQuery<TOut, TQuery> : IQueryHandler<TOut>
    {
        private readonly IMaybeStatefulHandler _inner;
        protected readonly TQuery _query;
        protected readonly HardCodedParameters _hardcoded;

        public ComplexCompiledQuery(IMaybeStatefulHandler inner, TQuery query, HardCodedParameters hardcoded)
        {
            _inner = inner;
            _query = query;
            _hardcoded = hardcoded;
        }

        public abstract void ConfigureCommand(CommandBuilder builder, IMartenSession session);

        public abstract IQueryHandler<TOut> BuildHandler(IMartenSession session);

        public TOut Handle(DbDataReader reader, IMartenSession session)
        {
            var inner = BuildHandler(session);
            return inner.Handle(reader, session);
        }

        public Task<TOut> HandleAsync(DbDataReader reader, IMartenSession session, CancellationToken token)
        {
            var inner = BuildHandler(session);
            return inner.HandleAsync(reader, session, token);
        }
    }
}
