using System.Linq.Expressions;
using Remotion.Linq.Clauses;
using Remotion.Linq.Clauses.StreamedData;

namespace Marten.Linq
{
    public class OrderByComparerOperator
        : OrderByOperator
    {
        public OrderByComparerOperator(
            Expression parameter,
            ConstantExpression comparerExpression): base(parameter)
        {
            ComparerExpression = comparerExpression;
        }

        public ConstantExpression ComparerExpression { get; }

        public override ResultOperatorBase Clone(CloneContext cloneContext)
        {
            return new OrderByComparerOperator(Parameter, ComparerExpression);
        }

        public override StreamedSequence ExecuteInMemory<T>(StreamedSequence input)
        {
            return input;
        }
    }
}
