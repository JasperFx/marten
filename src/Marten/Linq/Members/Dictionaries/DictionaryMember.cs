using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Marten.Exceptions;
using Marten.Internal;
using Marten.Linq.Parsing;
using Marten.Linq.SqlGeneration;
using Marten.Linq.SqlGeneration.Filters;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Members.Dictionaries;

internal class DictionaryMember<TKey, TValue>: QueryableMember, IComparableMember, IDictionaryMember, ICollectionMember
{
    private readonly StoreOptions _options;
    private readonly DictionaryKeysMember _keys;
    private readonly DictionaryValuesMember _values;
    private readonly KeyValuePairMemberCollection<TKey,TValue> _members;

    public DictionaryMember(StoreOptions options, IQueryableMember parent, Casing casing, MemberInfo member): base(parent, casing, member)
    {
        _options = options;
        RawLocator = $"{parent.JSONBLocator} ->> '{MemberName}'";
        TypedLocator = $"{parent.JSONBLocator} -> '{MemberName}'";

        IsEmpty = new DictionaryIsEmpty(this);
        NotEmpty = new DictionaryIsNotEmpty(this);

        _keys = new DictionaryKeysMember(this, options);
        _values = new DictionaryValuesMember(this, options);

        Count = new DictionaryCountMember(this);

        _members = new KeyValuePairMemberCollection<TKey, TValue>(options);

    }

    public DictionaryCountMember Count { get; }

    public Type ValueType => typeof(TValue);
    public Type KeyType => typeof(TKey);

    public IQueryableMember Element =>
        throw new BadLinqExpressionException("Marten cannot (yet) support queries against dictionary pairs");

    public ISqlFragment IsEmpty { get; }
    public ISqlFragment NotEmpty { get; }

    public override IQueryableMember FindMember(MemberInfo member)
    {
        if (member.Name == "Values") return _values;
        if (member.Name == "Keys") return _keys;

        if (member.Name == "Count" || member.Name == "Length")
        {
            return Count;
        }

        return base.FindMember(member);
    }

    public IQueryableMember MemberForKey(object keyValue)
    {
        var key = (TKey)keyValue;
        return new DictionaryItemMember<TKey, TValue>(this, key);
    }

    public override void PlaceValueInDictionaryForContainment(Dictionary<string, object> dict,
        ConstantExpression constant)
    {
        if (constant.Value is TValue)
        {
            base.PlaceValueInDictionaryForContainment(dict, constant);
        }
        else if (constant.Value is KeyValuePair<TKey, TValue> pair)
        {
            var childDict = new Dictionary<TKey, TValue>();
            childDict[pair.Key] = pair.Value;

            dict[MemberName] = childDict;
        }
        else
        {
            throw new BadLinqExpressionException("Marten can not (yet) support value " + constant.Value +
                                                 " in a search involving a dictionary member");
        }
    }

    Type ICollectionMember.ElementType => typeof(KeyValuePair<TKey, TValue>);

    string ICollectionMember.ExplodeLocator => throw new NotImplementedException();

    string ICollectionMember.ArrayLocator => throw new NotImplementedException();

    SelectorStatement ICollectionMember.BuildSelectManyStatement(CollectionUsage collectionUsage, IMartenSession session,
        SelectorStatement parentStatement)
    {
        throw new BadLinqExpressionException(
            "Sorry, Marten can not (yet) explode dictionary pairs in a LINQ SelectMany() expression, you will have to do that in memory");
    }

    ISelectClause ICollectionMember.BuildSelectClauseForExplosion(string fromObject)
    {
        throw new BadLinqExpressionException(
            "Sorry, Marten can not (yet) explode dictionary pairs in a LINQ SelectMany() expression, you will have to do that in memory");
    }

    ISqlFragment ICollectionMember.ParseWhereForAny(Expression body, IReadOnlyStoreOptions options)
    {
        var whereClause = new ChildCollectionWhereClause();
        var parser = new WhereClauseParser((StoreOptions)options, _members, whereClause);
        parser.Visit(body);

        if (whereClause.Fragment is MemberComparisonFilter filter)
        {
            if (filter.Member.MemberName == "Key" && filter.Op == "=" && filter.Right is CommandParameter p)
            {
                return new DictionaryContainsKeyFilter(this, _options.Serializer(), p.Value);
            }

            if (filter.Member.MemberName == "Value" && filter.Op == "=" && filter.Right is CommandParameter p1)
            {
                return new DictionaryValuesContainFilter(this, _options.Serializer(), p1.Value);
            }
        }

        throw new BadLinqExpressionException(
            $"Marten does not (yet) support the expression '{body}' within a Dictionary.Any() clause");
    }

    IComparableMember ICollectionMember.ParseComparableForCount(Expression body)
    {
        throw new BadLinqExpressionException(
            "Marten does not (yet) support complex Count() queries against dictionaries");
    }

    ISqlFragment ICollectionMember.ParseWhereForAll(MethodCallExpression body, IReadOnlyStoreOptions options)
    {
        throw new BadLinqExpressionException(
            "Querying against dictionaries with the All() operator is not (yet) supported");
    }

    ISqlFragment ICollectionMember.ParseWhereForContains(MethodCallExpression body, IReadOnlyStoreOptions options)
    {
        var constant = body.Arguments.Single().ReduceToConstant();

        return new ContainmentWhereFilter(this, constant, options.Serializer());
    }
}
