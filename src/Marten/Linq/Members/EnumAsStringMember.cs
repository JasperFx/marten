using System;
using System.Linq.Expressions;
using System.Reflection;
using Marten.Exceptions;
using Marten.Linq.SqlGeneration.Filters;
using NpgsqlTypes;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Members;

public class EnumAsStringMember: QueryableMember, IComparableMember
{
    public EnumAsStringMember(IQueryableMember parent, Casing casing, MemberInfo member): base(parent, casing, member)
    {
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

        var stringValue = Enum.GetName(MemberType, constant.Value);
        return new MemberComparisonFilter(this, new CommandParameter(stringValue, NpgsqlDbType.Varchar), op);
    }

    public override string SelectorForDuplication(string pgType)
    {
        return RawLocator.Replace("d.", "");
    }
}
