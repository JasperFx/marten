using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Marten.Internal.CompiledQueries;

/// <summary>
/// Creates sequences of unique values for different <see cref="Type"/>s
/// </summary>
public class UniqueValueSource
{
    private readonly Dictionary<Type, Queue<object>> _values = new();

    /// <summary>
    /// Returns the next unique value for the <see cref="Type"/> <paramref name="type"/>
    /// </summary>
    /// <param name="type">The type for which to get a unique value</param>
    /// <returns>The next unique value in the sequence</returns>
    /// <exception cref="InvalidOperationException">If there is no source for the <see cref="Type"/> <paramref name="type"/></exception>
    public object GetValue(Type type)
    {
        if (_values.TryGetValue(type, out var queue))
        {
            return queue.Dequeue();
        }

        queue = QueryCompiler.Finders.Single(x => x.Matches(type))
            .UniqueValueQueue(type);

        _values.Add(type, queue);

        return queue.Dequeue();
    }

    /// <summary>
    /// Returns an array of unique values that satisfy the constructor arguments of <paramref name="constructor"/>
    /// </summary>
    /// <param name="constructor">The constructor for which to create arguments</param>
    /// <returns>An array of unique valued arguments</returns>
    public object[] ArgsFor(ConstructorInfo constructor)
    {
        return constructor.GetParameters().Select(parameter => GetValue(parameter.ParameterType))
            .ToArray();
    }
}
