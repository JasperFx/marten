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

    /// <summary>
    ///     Legacy shape, kept so this stays binary compatible. A member built this way has no serializer
    ///     to ask and falls back to the enum's declared name, which is wrong for a member renamed with
    ///     <c>[JsonStringEnumMemberName]</c> / <c>[EnumMember]</c> (#5376). Prefer the overload that
    ///     takes the serializer.
    /// </summary>
    public EnumAsStringMember(IQueryableMember parent, Casing casing, MemberInfo member)
        : this(parent, casing, member, null)
    {
    }

    /// <param name="serializer">
    ///     Asked what it stores for a member, because it is the only component that knows. Marten's own
    ///     member factory always passes it.
    /// </param>
    public EnumAsStringMember(IQueryableMember parent, Casing casing, MemberInfo member, ISerializer? serializer)
        : base(parent, casing, member)
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

        // #5376: the name the serializer stored, not the declared one. They differ as soon as the
        // member was renamed with [JsonStringEnumMemberName] or [EnumMember], and comparing against
        // the declared name matches nothing while reporting it as "no rows".
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
