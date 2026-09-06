#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Marten.Internal.CompiledQueries;

internal class SimpleParameterFinder<T>: IParameterFinder
{
    private readonly Func<int, T[]> _uniqueValues;

    public SimpleParameterFinder(Func<int, T[]> uniqueValues)
    {
        _uniqueValues = uniqueValues;
    }

    public Type DotNetType => typeof(T);

    public Queue<object> UniqueValueQueue(Type type)
    {
        return new Queue<object>(_uniqueValues(100).OfType<object>());
    }

    public bool Matches(Type memberType)
    {
        return memberType == DotNetType;
    }

    // #5328: both T and T[] are closed here at compile time, so Native AOT has native code for
    // PropertyQueryMember<T> / PropertyQueryMember<T[]> and never needs MakeGenericType.
    public IQueryMember? BuildQueryMember(MemberInfo member, Type memberType)
    {
        if (memberType == typeof(T))
        {
            return member is PropertyInfo property
                ? new PropertyQueryMember<T>(property)
                : new FieldQueryMember<T>((FieldInfo)member);
        }

        if (memberType == typeof(T[]))
        {
            return member is PropertyInfo arrayProperty
                ? new PropertyQueryMember<T[]>(arrayProperty)
                : new FieldQueryMember<T[]>((FieldInfo)member);
        }

        return null;
    }

    public bool AreValuesUnique(object query, CompiledQueryPlan plan)
    {
        var members = findMembers(plan);

        if (members.Length == 0)
        {
            return true;
        }

        return members.Select(x => x.GetValue(query))
            .Distinct().Count() == members.Length;
    }

    private static IQueryMember<T>[] findMembers(CompiledQueryPlan plan)
    {
        return plan.QueryMembers.OfType<IQueryMember<T>>().ToArray();
    }
}
