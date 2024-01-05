using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using JasperFx.CodeGeneration;
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

internal abstract class QueryMember<T>: IQueryMember<T>
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
        if (Type.IsEnum)
        {
            var parameterValue = options.Serializer().EnumStorage == EnumStorage.AsInteger
                ? Value.As<int>()
                : (object)Value.ToString();

            return tryToFind(parameter, filters, parameterValue, out filter);
        }

        return tryToFind(parameter, filters, Value, out filter);
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

    private bool tryToFind(NpgsqlParameter parameter, ICompiledQueryAwareFilter[] filters,
        object value, out ICompiledQueryAwareFilter? filterUsed)
    {
        if (filters.All(x => x.ParameterName != parameter.ParameterName) && value.Equals(parameter.Value))
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
