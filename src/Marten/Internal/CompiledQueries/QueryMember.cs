using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using JasperFx.Core.Reflection;
using Npgsql;
using Weasel.Core;

namespace Marten.Internal.CompiledQueries;

internal interface IQueryMember<T>: IQueryMember
{
    T Value { get; }
    T GetValue(object query);
    void SetValue(object query, T value);
}

/// <summary>
///     A scalar query member that can stand for one element of an array the plan built inline,
///     <c>new[] { First, Second }</c>. See <c>CompiledQueryPlan.tryBindArrayElements</c>.
/// </summary>
internal interface IArrayElementSource
{
    /// <summary>The planning-time value, as the plan would have bound it.</summary>
    object? ElementValue(StoreOptions options, bool asStored);

    /// <summary>The same value read off a query instance at execution time.</summary>
    object? ElementValueFrom(object query, StoreOptions options, bool asStored);
}

internal abstract class QueryMember<T>: IQueryMember<T>, IArrayElementSource
{
    protected QueryMember(MemberInfo member)
    {
        Member = member;
    }

    public string Mask { get; set; }

    public Type Type => typeof(T);

    public object GetValueAsObject(object query)
    {
        return GetValue(query);
    }


    public abstract T GetValue(object query);
    public abstract void SetValue(object query, T value);

    public void StoreValue(object query)
    {
        Value = GetValue(query);
    }

    public bool TryMatch(NpgsqlParameter parameter, StoreOptions options, ICompiledQueryAwareFilter[] filters,
        out ICompiledQueryAwareFilter filter)
    {
        return tryToFind(parameter, filters, comparableValue(options), out filter);
    }

    public void MarkFilterUsages(StoreOptions options, ICompiledQueryAwareFilter[] filters)
    {
        var value = comparableValue(options);
        if (value == null)
        {
            return;
        }

        foreach (var filter in filters)
        {
            filter.TryMatchValue(value, Member);
        }
    }

    private object comparableValue(StoreOptions options)
    {
        return toComparable(Value, options);
    }

    private object toComparable(T value, StoreOptions options)
    {
        if (Type.IsEnum)
        {
            return options.Serializer().EnumStorage == EnumStorage.AsInteger
                ? value.As<int>()
                : value.ToString();
        }

        return value;
    }

    // asStored: the element as the serializer stores it (an enum as its number or name), which is
    // how an array of enums reaches the SQL; otherwise the raw member value.
    object? IArrayElementSource.ElementValue(StoreOptions options, bool asStored)
        => asStored ? toComparable(Value, options) : Value;

    object? IArrayElementSource.ElementValueFrom(object query, StoreOptions options, bool asStored)
    {
        var value = GetValue(query);
        return asStored ? toComparable(value, options) : value;
    }

    public void TryWriteValue(UniqueValueSource valueSource, object query)
    {
        if (CanWrite())
        {
            var value = (T)valueSource.GetValue(typeof(T));
            Value = value;
            SetValue(query, value);
        }
    }

    public T Value { get; private set; }

    public abstract bool CanWrite();

    public MemberInfo Member { get; }

    private static bool valuesAreEqual(object value, object? parameterValue)
    {
        if (value.Equals(parameterValue)) return true;

        // For array types (string[], Guid[], int[], etc.), Equals() does reference comparison.
        // We need structural comparison to match array parameter values from compiled query planning.
        if (value is Array valueArray && parameterValue is Array paramArray)
        {
            if (valueArray.Length != paramArray.Length) return false;
            for (var i = 0; i < valueArray.Length; i++)
            {
                if (!Equals(valueArray.GetValue(i), paramArray.GetValue(i))) return false;
            }
            return true;
        }

        return false;
    }

    private bool tryToFind(NpgsqlParameter parameter, ICompiledQueryAwareFilter[] filters,
        object value, out ICompiledQueryAwareFilter? filterUsed)
    {
        if (filters.All(x => x.ParameterName != parameter.ParameterName) && valuesAreEqual(value, parameter.Value))
        {
            filterUsed = null;
            return true;
        }

        foreach (var filter in filters)
        {
            if (filter.TryMatchValue(value, Member) && filter.ParameterName == parameter.ParameterName)
            {
                filterUsed = filter;
                return true;
            }
        }

        filterUsed = default;
        return false;
    }
}
