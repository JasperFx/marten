using System;
using System.Collections.Generic;

namespace Marten.Internal.CompiledQueries;

internal interface IParameterFinder
{
    bool Matches(Type memberType);

    /// <summary>
    /// Checks whether all values that can be found on <paramref name="query"/> by this <see cref="IParameterFinder"/>
    /// have unique values when compared amongst each other
    /// </summary>
    /// <param name="query">The query object to check</param>
    /// <param name="plan">The query plan built for <paramref name="query"/></param>
    /// <returns>True if all values are unique, false otherwise</returns>
    bool AreValuesUnique(object query, CompiledQueryPlan plan);
    Queue<object> UniqueValueQueue(Type type);
}
