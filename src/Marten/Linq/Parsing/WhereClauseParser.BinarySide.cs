using System.Linq.Expressions;
using Marten.Exceptions;
using Marten.Linq.Fields;
using Marten.Linq.Filters;
using Marten.Linq.SqlGeneration;
using Remotion.Linq.Parsing;

namespace Marten.Linq.Parsing
{
    internal partial class WhereClauseParser
    {
        internal class BinarySide : RelinqExpressionVisitor
        {
            public BinarySide(Expression memberExpression)
            {
                MemberExpression = memberExpression;
            }

            public ConstantExpression Constant { get; set; }
            public IField Field { get; set; }
            public IComparableFragment Comparable { get; set; }

            public Expression MemberExpression { get; }

            public ISqlFragment CompareTo(BinarySide right, string op)
            {
                if (Constant != null)
                {
                    return right.CompareTo(this, WhereClauseParser.OppositeOperators[op]);
                }

                if (Comparable != null && right.Constant != null) return Comparable.CreateComparison(op, right.Constant, MemberExpression);

                if (Field == null)
                    throw new BadLinqExpressionException("Unsupported binary value expression in a Where() clause");

                if (right.Constant != null)
                {
                    return Field.CreateComparison(op, right.Constant, MemberExpression);
                }

                if (right.Field != null)
                {
                    return new ComparisonFilter(Field, right.Field, op);
                }

                throw new BadLinqExpressionException("Unsupported binary value expression in a Where() clause");
            }
        }
    }
}
