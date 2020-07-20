using System;
using System.Collections.Generic;

namespace Marten.Internal.CompiledQueries
{
    public interface IParameterFinder
    {
        bool Matches(Type memberType);
        bool AreValuesUnique(object query, CompiledQueryPlan plan);
        Queue<object> UniqueValueQueue(Type type);
    }
}
