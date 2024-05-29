#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using JasperFx.CodeGeneration;
using Marten.Exceptions;
using Marten.Internal.CompiledQueries;
using Marten.Linq.Members;
using Marten.Linq.Members.Dictionaries;
using NpgsqlTypes;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.SqlGeneration.Filters;

public enum ContainmentUsage
{
    Singular,
    Collection
}

public class ContainmentWhereFilter: ICollectionAwareFilter, ICollectionAware, ICompiledQueryAwareFilter, IReversibleWhereFragment
{
    private readonly string _locator;
    private readonly ISerializer _serializer;
    private Dictionary<string, object> _data = new();
    private readonly List<DictionaryValueUsage> _usages = new();

    public static ContainmentWhereFilter ForValue(ICollectionMember member, object value, ISerializer serializer)
    {
        return new ContainmentWhereFilter(member, Expression.Constant(value), serializer);
    }

    public ContainmentWhereFilter(IQueryableMember member, ConstantExpression constant, ISerializer serializer)
    {
        _locator = member.Ancestors[0].RawLocator;
        _serializer = serializer;

        _usages.Add(new DictionaryValueUsage(constant.Value));

        PlaceMemberValue(member, constant);
    }

    public ContainmentWhereFilter(ICollectionMember collection, ISerializer serializer)
    {
        if (collection is DictionaryValuesMember)
            throw new BadLinqExpressionException(
                "Marten cannot (yet) support sub query filters against Dictionary<,>.Values. You will have to revert to using MatchesSql()");

        _locator = collection.JSONBLocator;
        _serializer = serializer;
        CollectionMember = collection;
    }

    public ISqlFragment MoveUnder(ICollectionMember ancestorCollection)
    {
        var dict = new Dictionary<string, object>();

        ancestorCollection.PlaceValueInDictionaryForContainment(dict, Expression.Constant(_data));

        _data = dict;

        foreach (var parent in ancestorCollection.Ancestors.Reverse())
        {
            if (parent is DocumentQueryableMemberCollection) break;

            dict = new Dictionary<string, object>();
            parent.PlaceValueInDictionaryForContainment(dict, Expression.Constant(_data));
            _data = dict;
        }

        return this;
    }

    public bool IsNot { get; set; }

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

    public void BuildJsonPathFilter(ICommandBuilder builder, Dictionary<string, object> parameters)
    {
        throw new NotSupportedException();
    }

    public ICollectionMember CollectionMember { get; }



    public void Apply(ICommandBuilder builder)
    {
        var json = Usage == ContainmentUsage.Singular
            ? _serializer.ToCleanJson(_data)
            : _serializer.ToCleanJson(new object[] { _data });

        if (IsNot)
        {
            builder.Append("NOT(");
        }

        builder.Append($"{_locator} @> ");
        builder.AppendParameter(json, NpgsqlDbType.Jsonb);

        ParameterName = builder.LastParameterName;

        if (IsNot)
        {
            builder.Append(")");
        }
    }

    public ISqlFragment Reverse()
    {
        IsNot = !IsNot;
        return this;
    }

    public void PlaceMemberValue(IQueryableMember member, ConstantExpression constant)
    {
        _usages.Add(new DictionaryValueUsage(constant.Value!));

        var dict = _data;
        for (var i = 1; i < member.Ancestors.Length; i++)
        {
            dict = member.Ancestors[i].FindOrPlaceChildDictionaryForContainment(dict);
        }

        member.PlaceValueInDictionaryForContainment(dict, constant);
    }

    public bool TryMatchValue(object value, MemberInfo member)
    {
        var match = _usages.FirstOrDefault(x => x.Value.Equals(value));
        if (match != null)
        {
            match.QueryMember = member;
            return true;
        }

        return false;
    }

    private bool _hasGenerated;

    public void GenerateCode(GeneratedMethod method, int parameterIndex, string parametersVariableName)
    {
        if (_hasGenerated)
        {
            return;
        }

        _hasGenerated = true;

        var top = new DictionaryDeclaration();
        top.ReadDictionary(_data, _usages);

        var part = Usage == ContainmentUsage.Singular ? (IDictionaryPart)top : new ArrayContainer(top);

        method.Frames.Add(new WriteSerializedJsonParameterFrame(parametersVariableName, parameterIndex, part));
    }

    public string ParameterName { get; private set; }

    public IEnumerable<DictionaryValueUsage> Values()
    {
        return _usages;
    }
}
