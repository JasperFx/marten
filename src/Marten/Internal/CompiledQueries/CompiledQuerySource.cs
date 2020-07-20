using System;
using Marten.Internal.Linq;

namespace Marten.Internal.CompiledQueries
{
    public abstract class CompiledQuerySource<TOut, TQuery> : ICompiledQuerySource
    {
        public Type QueryType => typeof(TQuery);

        public abstract IQueryHandler<TOut> BuildHandler(TQuery query, IMartenSession session);

        public IQueryHandler Build(object query, IMartenSession session)
        {
            return BuildHandler((TQuery)query, session);
        }
    }
}
