using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Marten.Linq.Members;
using NpgsqlTypes;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.SqlGeneration.Filters;

public enum ContainmentUsage
{
    Singular,
    Collection
}

public class ContainmentWhereFilter: ICollectionAwareFilter, ICollectionAware
{
    private readonly string _locator;
    private readonly ISerializer _serializer;
    private Dictionary<string, object> _data = new();

    public ContainmentWhereFilter(IQueryableMember member, ConstantExpression constant, ISerializer serializer)
    {
        _locator = member.Ancestors[0].RawLocator;
        _serializer = serializer;

        PlaceMemberValue(member, constant);
    }

    public ContainmentWhereFilter(ICollectionMember collection, ISerializer serializer)
    {
        _locator = collection.JSONBLocator;
        _serializer = serializer;
        CollectionMember = collection;
    }

    public ContainmentUsage Usage { get; set; } = ContainmentUsage.Singular;

    bool ICollectionAware.CanReduceInChildCollection()
    {
        return true;
    }

    ICollectionAwareFilter ICollectionAware.BuildFragment(ICollectionMember member, ISerializer serializer)
    {
        var original = _data;
        _data = new Dictionary<string, object>();
        PlaceMemberValue(member, Expression.Constant(original));

        return this;
    }

    bool ICollectionAware.SupportsContainment()
    {
        return true;
    }

    void ICollectionAware.PlaceIntoContainmentFilter(ContainmentWhereFilter filter)
    {
        var dict = filter._data;
        foreach (var ancestor in CollectionMember.Ancestors)
            dict = ancestor.FindOrPlaceChildDictionaryForContainment(dict);

        if (dict.TryGetValue(CollectionMember.MemberName, out var raw) && raw is object[] data)
        {
            if (data.Length == 1 && data[0] is Dictionary<string, object> existing)
            {
                foreach (var pair in _data) existing[pair.Key] = pair.Value;
            }
        }
        else
        {
            dict[CollectionMember.MemberName] = new object[] { _data };
        }
    }

    public bool CanBeJsonPathFilter()
    {
        return false;
    }

    public void BuildJsonPathFilter(CommandBuilder builder, Dictionary<string, object> parameters)
    {
        throw new NotSupportedException();
    }

    public ICollectionMember CollectionMember { get; }

    public ISqlFragment MoveUnder(ICollectionMember ancestorCollection)
    {
        throw new NotSupportedException();
    }

    public void Apply(CommandBuilder builder)
    {
        var json = Usage == ContainmentUsage.Singular
            ? _serializer.ToCleanJson(_data)
            : _serializer.ToCleanJson(new object[] { _data });

        builder.Append($"{_locator} @> ");
        builder.AppendParameter(json, NpgsqlDbType.Jsonb);
    }

    public bool Contains(string sqlText)
    {
        return false;
    }

    public void PlaceMemberValue(IQueryableMember member, ConstantExpression constant)
    {
        var dict = _data;
        for (var i = 1; i < member.Ancestors.Length; i++)
        {
            dict = member.Ancestors[i].FindOrPlaceChildDictionaryForContainment(dict);
        }

        member.PlaceValueInDictionaryForContainment(dict, constant);
    }
}
