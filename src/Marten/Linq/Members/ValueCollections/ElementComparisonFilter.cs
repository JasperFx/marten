using System;
using System.Collections.Generic;
using JasperFx.Core.Reflection;
using Marten.Exceptions;
using Marten.Linq.Parsing;
using Marten.Linq.SqlGeneration.Filters;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Members.ValueCollections;

internal class ElementComparisonFilter: ISqlFragment, ICollectionAware
{
    public ElementComparisonFilter(object value, string op)
    {
        Value = value;
        Op = op;
    }

    public object Value { get; }
    public string Op { get; }

    public IEnumerable<DictionaryValueUsage> Values()
    {
        yield return new DictionaryValueUsage(Value);
    }

    bool ICollectionAware.CanReduceInChildCollection()
    {
        if (Value == null) return false;
        if (Value.GetType().IsDateTime()) return false;
        if (Value.GetType() == typeof(DateTimeOffset)) return false;
        return true;
    }

    ICollectionAwareFilter ICollectionAware.BuildFragment(ICollectionMember member, ISerializer serializer)
    {
        switch (Op)
        {
            case "=":
                return ContainmentWhereFilter.ForValue(member, Value, serializer);

            case "!=":
                return (ICollectionAwareFilter)ContainmentWhereFilter.ForValue(member, Value, serializer).Reverse();

            default:
                throw new BadLinqExpressionException(
                    $"Marten does not (yet) support the {Op} operator in element member queries");
        }
    }

    bool ICollectionAware.SupportsContainment()
    {
        // Little goofy. Make it do its own thing
        return false;
    }

    void ICollectionAware.PlaceIntoContainmentFilter(ContainmentWhereFilter filter)
    {
        throw new NotSupportedException();
    }

    public bool CanBeJsonPathFilter()
    {
        return true;
    }

    public void BuildJsonPathFilter(CommandBuilder builder, Dictionary<string, object> parameters)
    {
        var parameter = parameters.AddJsonPathParameter(Value);

        builder.Append("@ ");
        builder.Append(Op);
        builder.Append(" ");
        builder.Append(parameter);
    }

    void ISqlFragment.Apply(CommandBuilder builder)
    {
        throw new NotSupportedException();
    }

    bool ISqlFragment.Contains(string sqlText)
    {
        throw new NotSupportedException();
    }
}