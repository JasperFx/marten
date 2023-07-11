using System.Linq.Expressions;
using Marten.Linq.Members;

namespace Marten.Linq.Parsing.Operators;

public class Ordering
{
    public Ordering(Expression expression, OrderingDirection direction)
    {
        Expression = expression;
        Direction = direction;
    }

    public Expression Expression { get; }

    public OrderingDirection Direction { get; }

    public CasingRule CasingRule { get; set; } = CasingRule.CaseSensitive;

    public string BuildExpression(IQueryableMemberCollection collection)
    {
        var member = collection.MemberFor(Expression, "Invalid OrderBy() expression");

        return member.BuildOrderingExpression(this, CasingRule);
    }
}
