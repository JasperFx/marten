using System.Data.Common;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Marten.Linq.QueryHandlers;
using Weasel.Postgresql;
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
        public Task<int> StreamJson(Stream stream, DbDataReader reader, CancellationToken token)
        {
            return _inner.StreamJson(stream, reader, token);
        }

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
