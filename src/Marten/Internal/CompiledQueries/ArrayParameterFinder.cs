#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Marten.Internal.CompiledQueries;

/// <summary>
/// Parameter finder for array-typed query members (string[], Guid[], int[], etc.)
/// used in compiled queries with operators like IsOneOf().
/// </summary>
internal class ArrayParameterFinder<TElement> : IParameterFinder
{
    private readonly Func<int, TElement[]> _uniqueElementValues;

    public ArrayParameterFinder(Func<int, TElement[]> uniqueElementValues)
    {
        _uniqueElementValues = uniqueElementValues;
    }

    public Type DotNetType => typeof(TElement[]);

    public Queue<object> UniqueValueQueue(Type type)
    {
        // Each unique value is itself a TElement[] with unique content
        var queue = new Queue<object>();
        for (var i = 0; i < 20; i++)
        {
            queue.Enqueue(_uniqueElementValues(i + 1));
        }
        return queue;
    }

    public bool Matches(Type memberType)
    {
        return memberType == typeof(TElement[]);
    }

    // #5328: see IParameterFinder.BuildQueryMember — closed at compile time for Native AOT.
    public IQueryMember? BuildQueryMember(MemberInfo member, Type memberType)
    {
        if (memberType != typeof(TElement[]))
        {
            return null;
        }

        return member is PropertyInfo property
            ? new PropertyQueryMember<TElement[]>(property)
            : new FieldQueryMember<TElement[]>((FieldInfo)member);
    }

    public bool AreValuesUnique(object query, CompiledQueryPlan plan)
    {
        var members = plan.QueryMembers.OfType<IQueryMember<TElement[]>>().ToArray();

        if (members.Length == 0)
        {
            return true;
        }

        // For arrays, check that each member has a distinct array (by reference or content)
        return members.Select(x => x.GetValue(query))
            .Distinct(new ArrayContentComparer<TElement>())
            .Count() == members.Length;
    }
}

internal class ArrayContentComparer<T> : IEqualityComparer<T[]?>
{
    public bool Equals(T[]? x, T[]? y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (x == null || y == null) return false;
        return x.SequenceEqual(y);
    }

    public int GetHashCode(T[]? obj)
    {
        if (obj == null) return 0;
        unchecked
        {
            var hash = 17;
            foreach (var item in obj)
            {
                hash = hash * 31 + (item?.GetHashCode() ?? 0);
            }
            return hash;
        }
    }
}
