#nullable enable
using System.Linq.Expressions;
using System.Reflection;
using Marten.Exceptions;
using Marten.Linq.SqlGeneration.Filters;
using NpgsqlTypes;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Members;

public class EnumAsIntegerMember: QueryableMember, IComparableMember
{
    public EnumAsIntegerMember(IQueryableMember parent, Casing casing, MemberInfo member): base(parent, casing, member)
    {
        PgType = "integer";
        TypedLocator = $"CAST({RawLocator} as {PgType})";
    }

    public string PgType { get; set; }

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

        var integer = (int)constant.Value;
        return new MemberComparisonFilter(this, new CommandParameter(integer, NpgsqlDbType.Integer), op);
    }
}
