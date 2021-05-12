using System;
using System.Collections.Generic;

namespace Marten.Internal.CompiledQueries
{
    internal interface IParameterFinder
    {
        bool Matches(Type memberType);
        bool AreValuesUnique(object query, CompiledQueryPlan plan);
        Queue<object> UniqueValueQueue(Type type);
    }
}
