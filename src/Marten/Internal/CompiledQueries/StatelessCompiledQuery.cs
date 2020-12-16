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

        public StatelessCompiledQuery(IQueryHandler<TOut> inner)
        {
            _inner = inner;
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
