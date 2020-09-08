using System;
using Marten.Linq.QueryHandlers;

namespace Marten.Internal.CompiledQueries
{
    public interface ICompiledQuerySource
    {
        Type QueryType { get; }
        IQueryHandler Build(object query, IMartenSession session);
    }
}
