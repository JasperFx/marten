#nullable enable
using System;
using System.Linq.Expressions;
using System.Reflection;
using Marten.Exceptions;
using Marten.Linq.SqlGeneration.Filters;
using Marten.Util;
using NpgsqlTypes;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Members;

public class EnumAsStringMember: QueryableMember, IComparableMember
{
    private readonly ISerializer? _serializer;

    public EnumAsStringMember(IQueryableMember parent, Casing casing, MemberInfo member,
        ISerializer? serializer = null): base(parent, casing, member)
    {
        _serializer = serializer;

        if (!MemberType.IsEnum)
        {
            throw new ArgumentOutOfRangeException(nameof(member), "Not an Enum type");
        }
    }

    public override ISqlFragment CreateComparison(string op, ConstantExpression constant)
    {
        if (constant == null || constant.Value == null)
        {
            return op switch
            {
                "=" => new IsNullFilter(this),
                "!=" => new IsNotNullFilter(this),
                _ => throw new BadLinqExpressionException(
                    $"Unable to create a SQL filter for member {Member.Name} {op} NULL")
            };
        }

        // The name the serializer stored, not the declared one: they differ as soon as the member was
        // renamed with [JsonStringEnumMemberName] or [EnumMember] (#5376).
        var stringValue = _serializer is null
            ? Enum.GetName(MemberType, constant.Value)!
            : EnumMemberNames.Of(_serializer, MemberType, constant.Value);
        return new MemberComparisonFilter(this, new CommandParameter(stringValue, NpgsqlDbType.Varchar), op);
    }

    public override string SelectorForDuplication(string pgType)
    {
        return RawLocator.RemoveTableAlias("d");
    }
}
