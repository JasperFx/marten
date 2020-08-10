using System;
using System.Linq.Expressions;
using Remotion.Linq.Clauses;
using Remotion.Linq.Clauses.ResultOperators;
using Remotion.Linq.Clauses.StreamedData;

namespace Marten.Linq
{
    public class OrderByOperator
        : SequenceTypePreservingResultOperatorBase
    {
        public OrderByOperator(Expression parameter)
        {
            Parameter = parameter;
        }

        public Expression Parameter { get; private set; }

        public override ResultOperatorBase Clone(CloneContext cloneContext)
        {
            return new OrderByOperator(Parameter);
        }

        public override void TransformExpressions(
            Func<Expression, Expression> transformation)
        {
            Parameter = transformation(Parameter);
        }

        public override StreamedSequence ExecuteInMemory<T>(StreamedSequence input)
        {
            return input;
        }
    }
}
