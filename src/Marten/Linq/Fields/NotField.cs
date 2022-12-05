using System.Linq.Expressions;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Fields;

public class NotField: IComparableFragment
{
    private readonly IField _inner;

    public NotField(IField inner)
    {
        _inner = inner;
    }

    public ISqlFragment CreateComparison(string op, ConstantExpression value, Expression memberExpression)
    {
        var opposite = ComparisonFilter.NotOperators[op];
        return _inner.CreateComparison(opposite, value, memberExpression);
    }
}
