#nullable enable
using System.Linq.Expressions;
using JasperFx.Core;
using Marten.Linq.Members;

namespace Marten.Linq.Parsing.Operators;

public class Ordering
{
    public Ordering(Expression expression, OrderingDirection direction)
    {
        Expression = expression;
        Direction = direction;
    }

    public Ordering(string literal)
    {
        Literal = literal;
    }

    public Ordering(string memberName, OrderingDirection direction)
    {
        MemberName = memberName;
        Direction = direction;
    }

    public string? MemberName { get; set; }

    public string? Literal { get; }

    public Expression Expression { get; }

    public OrderingDirection Direction { get; set; }

    public CasingRule CasingRule { get; set; } = CasingRule.CaseSensitive;

    /// <summary>
    ///     Refers to whether or not this ordering is transformed such that it cannot
    ///     be combined with a Distinct(Select()) usage
    /// </summary>
    public bool IsTransformed { get; set; }

    public string BuildExpression(IQueryableMemberCollection collection)
    {
        if (Literal.IsNotEmpty())
        {
            return Literal;
        }

        var member = MemberName.IsNotEmpty()
            ? collection.MemberFor(MemberName)
            : collection.MemberFor(Expression, "Invalid OrderBy() expression");

        return member.BuildOrderingExpression(this, CasingRule);
    }
}
