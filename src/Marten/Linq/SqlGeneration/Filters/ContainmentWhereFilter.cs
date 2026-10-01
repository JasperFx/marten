#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Marten.Exceptions;
using Marten.Internal.CompiledQueries;
using Marten.Linq.Members;
using Marten.Linq.Members.Dictionaries;
using Npgsql;
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
    private string _locator;
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

    /// <summary>
    ///     Re-anchors this filter one collection level outwards, so that an enclosing
    ///     <c>Any()</c> can express it as containment against its own collection.
    /// </summary>
    /// <remarks>
    ///     A sub-query is parsed against a member tree rooted at the element type -- <c>Bottoms</c>
    ///     inside <c>Middles.Any(m =&gt; m.Bottoms.Any(...))</c> reports <c>d.data -&gt; 'Bottoms'</c>,
    ///     because inside that parse <c>d.data</c> stands for one <c>Middle</c>. Nothing in the member
    ///     tree records that <c>Bottoms</c> lives under <c>Middles</c>; this is where that nesting is
    ///     put back. The locator has to move with the payload (#5549): re-nesting the dictionary while
    ///     leaving <c>_locator</c> at the inner collection produced
    ///     <c>d.data -&gt; 'Bottoms' @&gt; '[{"Middles":[...]}]'</c>, which is well-formed SQL that can
    ///     never match, so the query quietly returned nothing.
    /// </remarks>
    public ISqlFragment MoveUnder(ICollectionMember ancestorCollection)
    {
        assertCanCarryContainment(ancestorCollection);

        // One level only: CompileFragment runs at every enclosing Any(), so a filter nested
        // n deep is moved up n times rather than walking the ancestors here. Walking them
        // was also unreachable for anything deeper than two levels -- the ancestor of a
        // sub-query's collection is a RootMember, whose PlaceValueInDictionaryForContainment
        // throws NotSupportedException.
        _data = payloadRelativeToElement();

        _locator = ancestorCollection.JSONBLocator;
        Usage = ContainmentUsage.Collection;
        CollectionMember = ancestorCollection;

        return this;
    }

    /// <summary>
    ///     A dictionary has no JSON key standing for "some value", so there is no containment payload
    ///     that reaches through one. The same refusal the constructor already makes for a
    ///     <c>Dictionary&lt;,&gt;.Values</c> sub-query, made here for anything moved under one.
    /// </summary>
    private static void assertCanCarryContainment(ICollectionMember ancestorCollection)
    {
        if (ancestorCollection is DictionaryValuesMember or IDictionaryMember ||
            ancestorCollection.Ancestors.Any(x => x is DictionaryValuesMember or IDictionaryMember))
        {
            throw new BadLinqExpressionException(
                "Marten cannot (yet) support a sub query filter nested inside a dictionary member. You may need to resort to MatchesSql(), and using the PostgreSQL '#>' JSONPath operator. See https://www.postgresql.org/docs/current/functions-json.html");
        }
    }

    /// <summary>
    ///     This filter's payload expressed relative to one element of the collection it is anchored
    ///     to, which is the shape an enclosing containment filter has to splice in.
    /// </summary>
    private Dictionary<string, object> payloadRelativeToElement()
    {
        // Singular means the payload is already written from the element outwards -- that is how a
        // value collection or a Contains() filter arrives, carrying its own member name.
        if (CollectionMember == null || Usage == ContainmentUsage.Singular)
        {
            return _data;
        }

        var root = new Dictionary<string, object>();
        var dict = root;

        // Any plain members between the element and this filter's collection, as in
        // m.Inner.Bottoms.Any(...). The document root carries no key of its own.
        foreach (var ancestor in CollectionMember.Ancestors)
        {
            if (ancestor is DocumentQueryableMemberCollection) continue;
            dict = ancestor.FindOrPlaceChildDictionaryForContainment(dict);
        }

        CollectionMember.PlaceValueInDictionaryForContainment(dict, Expression.Constant(_data));

        return root;
    }

    public bool IsNot { get; set; }

    public ContainmentUsage Usage { get; set; } = ContainmentUsage.Singular;

    bool ICollectionAware.CanReduceInChildCollection()
    {
        return true;
    }

    ICollectionAwareFilter ICollectionAware.BuildFragment(ICollectionMember member, ISerializer serializer)
    {
        // Reached when an OR branch inside Any() is itself a nested containment. Same move as
        // MoveUnder(), and it has to re-anchor the locator for the same reason (#5549).
        if (ReferenceEquals(CollectionMember, member))
        {
            return this;
        }

        return (ICollectionAwareFilter)MoveUnder(member);
    }

    bool ICollectionAware.SupportsContainment()
    {
        return true;
    }

    void ICollectionAware.PlaceIntoContainmentFilter(ContainmentWhereFilter filter)
    {
        // Both filters describe one element of the same collection, so this one's payload is spliced
        // into the target's. A value-collection filter (CollectionMember == null) already carries its
        // own member name and merges in as it stands.
        mergeInto(filter._data, payloadRelativeToElement());

        filter._usages.AddRange(_usages);
    }

    private static void mergeInto(Dictionary<string, object> target, Dictionary<string, object> source)
    {
        foreach (var pair in source)
        {
            if (!target.TryGetValue(pair.Key, out var existing))
            {
                target[pair.Key] = pair.Value;
            }
            else if (existing is Dictionary<string, object> existingDict &&
                     pair.Value is Dictionary<string, object> incomingDict)
            {
                mergeInto(existingDict, incomingDict);
            }
            else if (existing is object[] existingArray && pair.Value is object[] incomingArray)
            {
                // Two sibling Any() calls over the same child collection. `@>` over arrays asks that
                // every element of the payload be matched by *some* element of the stored array, which
                // is exactly what the two calls mean. Marten used to merge them into a single element
                // instead, which both demanded one element satisfy both predicates and silently
                // dropped whichever value was placed first (#5549).
                target[pair.Key] = existingArray.Concat(incomingArray).ToArray();
            }
            else
            {
                target[pair.Key] = pair.Value;
            }
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

    public ICollectionMember CollectionMember { get; private set; }



    public void Apply(ICommandBuilder builder)
    {
        var json = Usage == ContainmentUsage.Singular
            ? JsonbPayload.ToJson(_serializer, _data)
            : JsonbPayload.ToJson(_serializer, new object[] { _data });

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

    public Action<NpgsqlParameter, object> BuildSetter()
    {
        // Snapshot the per-filter state at plan-construction time. The captured
        // _data tree references DictionaryValueUsage entries whose QueryMember
        // was assigned during the matching TryMatchValue call; the setter walks
        // the tree per session.Query(...) invocation and substitutes each leaf
        // value with the corresponding member read off the user's query
        // instance. AOT-clean: no reflection emit, no Expression compile.
        var data = _data;
        var usages = _usages;
        var serializer = _serializer;
        var wrapInArray = Usage == ContainmentUsage.Collection;
        return (parameter, query) =>
        {
            var dict = CompiledQueryDictionaryBuilder.Build(data, usages, query);
            var payload = wrapInArray ? (object)new object[] { dict } : dict;
            parameter.NpgsqlDbType = NpgsqlDbType.Jsonb;
            parameter.Value = JsonbPayload.ToJson(serializer, payload);
        };
    }

    public string ParameterName { get; private set; }

    public IEnumerable<DictionaryValueUsage> Values()
    {
        return _usages;
    }
}
