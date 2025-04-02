using System.Linq.Expressions;
using Marten.Exceptions;
using Marten.Linq.Members;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Parsing;

internal class StringCompareComparable: IComparableMember
{
    private readonly SimpleExpression _left;
    private readonly SimpleExpression _right;

    internal StringCompareComparable(SimpleExpression left, SimpleExpression right)
    {
        _left = left;
        _right = right;
    }

    public ISqlFragment CreateComparison(string op, ConstantExpression constant)
    {
        if (constant.Value is int intValue && intValue == 0)
        {
            var leftFragment = _left.FindValueFragment();
            var rightFragment = _right.FindValueFragment();

            return new ComparisonFilter(leftFragment, rightFragment, op);
        }
        else
        {
            throw new BadLinqExpressionException("string.Compare must be compared to 0");
        }
    }
}
