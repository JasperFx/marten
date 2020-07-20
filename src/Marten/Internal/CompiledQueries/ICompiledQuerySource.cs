using System;
using Marten.Internal.Linq;

namespace Marten.Internal.CompiledQueries
{
    public interface ICompiledQuerySource
    {
        Type QueryType { get; }
        IQueryHandler Build(object query, IMartenSession session);
    }
}
