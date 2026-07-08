#nullable enable
using System.Linq.Expressions;
using Marten.Exceptions;
using Marten.Linq.Parsing;
using Weasel.Core;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Members.Dictionaries;

internal class DictionaryItemMember<TKey, TValue>: QueryableMember, IComparableMember where TKey: notnull where TValue: notnull
{
    public DictionaryItemMember(DictionaryMember<TKey, TValue> parent, TKey key)
        : base(parent, key.ToString(), typeof(TValue))
    {
        Parent = parent;
        Key = key;

        // The key is a runtime value (see SimpleExpression: it comes from the indexer
        // argument evaluated at query-build time, so it is NOT restricted to compile-time
        // literals and can be attacker-influenced in "filter by attribute name" patterns).
        // It is inlined into a single-quoted JSON-path literal, so escape embedded single
        // quotes to keep it as data and prevent SQL injection — mirroring the escaping in
        // Ordering.BuildNgramRankExpression. The comparison value is already parameterized.
        var escapedKey = key.ToString()!.Replace("'", "''");
        RawLocator = TypedLocator = $"{Parent.TypedLocator} ->> '{escapedKey}'";

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
