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
using Weasel.Core;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Members;

internal interface IDictionaryMember
{
    IQueryableMember MemberForKey(object keyValue);
}

internal class DictionaryMember<TKey, TValue>: QueryableMember, IComparableMember, IDictionaryMember, ICollectionMember
{
    public DictionaryMember(IQueryableMember parent, Casing casing, MemberInfo member): base(parent, casing, member)
    {
        RawLocator = $"{parent.JSONBLocator} ->> '{MemberName}'";
        TypedLocator = $"{parent.JSONBLocator} -> '{MemberName}'";
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

    SelectorStatement ICollectionMember.BuildChildStatement(CollectionUsage collectionUsage, IMartenSession session,
        SelectorStatement parentStatement)
    {
        throw new NotImplementedException();
    }

    ISelectClause ICollectionMember.BuildSelectClauseForExplosion(string fromObject)
    {
        throw new NotImplementedException();
    }

    ISqlFragment ICollectionMember.ParseWhereForAny(Expression body, IReadOnlyStoreOptions options)
    {
        throw new NotImplementedException();
    }

    IComparableMember ICollectionMember.ParseComparableForCount(Expression body)
    {
        throw new NotImplementedException();
    }

    ISqlFragment ICollectionMember.ParseWhereForAll(MethodCallExpression body, IReadOnlyStoreOptions options)
    {
        throw new NotImplementedException();
    }

    ISqlFragment ICollectionMember.ParseWhereForContains(MethodCallExpression body, IReadOnlyStoreOptions options)
    {
        var constant = body.Arguments.Single().ReduceToConstant();

        return new ContainmentWhereFilter(this, constant, options.Serializer());
    }
}

internal class DictionaryItemMember<TKey, TValue>: QueryableMember, IComparableMember
{
    public DictionaryItemMember(DictionaryMember<TKey, TValue> parent, TKey key)
        : base(parent, key.ToString(), typeof(TValue))
    {
        Parent = parent;
        Key = key;

        RawLocator = TypedLocator = $"{Parent.TypedLocator} ->> '{key}'";

        if (typeof(TValue) != typeof(string))
        {
            // Not supporting enums here anyway
            var pgType = PostgresqlProvider.Instance.GetDatabaseType(MemberType, EnumStorage.AsInteger);

            TypedLocator = $"CAST({TypedLocator} as {pgType})";
        }
    }

    public DictionaryMember<TKey, TValue> Parent { get; }
    public TKey Key { get; }

    public override ISqlFragment CreateComparison(string op, ConstantExpression constant)
    {
        if (typeof(TValue) == typeof(object))
        {
            var value = constant.Value();
            if (value == null)
            {
                return base.CreateComparison(op, constant);
            }

            var valueType = value.GetType();
            if (valueType.IsEnum)
            {
                throw new BadLinqExpressionException(
                    "Marten does not (yet) support enumeration values as Dictionary values");
            }

            var pgType = PostgresqlProvider.Instance.GetDatabaseType(valueType, EnumStorage.AsInteger);
            return new WhereFragment($"CAST({TypedLocator} as {pgType}) {op} ?", value);
        }

        return base.CreateComparison(op, constant);
    }
}
