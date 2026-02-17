using System.Linq.Expressions;
using Marten.Exceptions;
using Marten.Linq.Members;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Parsing;

internal class CompareToComparable: IComparableMember
{
    private readonly SimpleExpression _left;
    private readonly SimpleExpression _right;

    public CompareToComparable(SimpleExpression left, SimpleExpression right)
    {
        _left = left;
        _right = right;
    }

    public ISqlFragment CreateComparison(string op, ConstantExpression constant)
    {
        // Only compare to 0 is valid: CompareTo() > 0 → ">", CompareTo() == 0 → "=", CompareTo() < 0 → "<"
        if (constant.Value is int intValue && intValue == 0)
        {
            var leftFragment = _left.FindValueFragment();
            var rightFragment = _right.FindValueFragment();

            return new ComparisonFilter(leftFragment, rightFragment, op);
        }

        throw new BadLinqExpressionException(
            "string.CompareTo() must be compared to 0 (e.g., x.Name.CompareTo(\"A\") > 0)");
    }
}

