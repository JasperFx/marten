using System.Linq.Expressions;
using JasperFx.Core;
using Marten.Linq.Members;

namespace Marten.Linq.Parsing.Operators;

public class Ordering
{
    private readonly string _literal;

    public Ordering(Expression expression, OrderingDirection direction)
    {
        Expression = expression;
        Direction = direction;
    }

    public Ordering(string literal)
    {
        _literal = literal;
    }

    public string Literal => _literal;

    public Expression Expression { get; }

    public OrderingDirection Direction { get; }

    public CasingRule CasingRule { get; set; } = CasingRule.CaseSensitive;

    /// <summary>
    /// Refers to whether or not this ordering is transformed such that it cannot
    /// be combined with a Distinct(Select()) usage
    /// </summary>
    public bool IsTransformed { get; set; }

    public string BuildExpression(IQueryableMemberCollection collection)
    {
        if (_literal.IsNotEmpty()) return _literal;

        var member = collection.MemberFor(Expression, "Invalid OrderBy() expression");

        return member.BuildOrderingExpression(this, CasingRule);
    }
}
