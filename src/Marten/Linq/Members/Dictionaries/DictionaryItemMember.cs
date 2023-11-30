using System.Linq.Expressions;
using Marten.Exceptions;
using Marten.Linq.Parsing;
using Weasel.Core;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Members.Dictionaries;

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