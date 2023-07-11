using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using JasperFx.CodeGeneration;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Util;
using Npgsql;
using NpgsqlTypes;
using Weasel.Core;
using Weasel.Postgresql;

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

    public void TryMatch(List<NpgsqlParameter> parameters, ICompiledQueryAwareFilter[] filters,
        StoreOptions storeOptions)
    {
        if (Type.IsEnum)
        {
            var parameterValue = storeOptions.Serializer().EnumStorage == EnumStorage.AsInteger
                ? Value.As<int>()
                : (object)Value.ToString();

            tryToFind(parameters, filters, parameterValue);
        }

        tryToFind(parameters, filters, Value);
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

    public List<CompiledParameterApplication> Usages { get; } = new();

    public void GenerateCode(GeneratedMethod method, StoreOptions storeOptions)
    {
        foreach (var usage in Usages)
        {
            usage.GenerateCode(method, storeOptions, Member);
        }
    }

    private bool tryToFind(List<NpgsqlParameter> parameters, ICompiledQueryAwareFilter[] filters,
        object value)
    {
        for (var i = 0; i < parameters.Count; i++)
        {
            var parameter = parameters[i];
            if (filters.All(x => x.ParameterName != parameter.ParameterName) && value.Equals(parameter.Value))
            {
                Usages.Add(new CompiledParameterApplication(i, null));
            }
        }

        foreach (var filter in filters)
        {
            var parameter = parameters.FirstOrDefault(x => x.ParameterName == filter.ParameterName);
            if (filter.TryMatchValue(value, Member))
            {
                var index = parameters.IndexOf(parameter);
                Usages.Add(new CompiledParameterApplication(index, filter));
            }
        }

        return Usages.Any();
    }
}
