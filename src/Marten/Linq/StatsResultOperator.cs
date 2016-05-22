using System;
using System.Linq.Expressions;
using Remotion.Linq.Clauses;
using Remotion.Linq.Clauses.ResultOperators;
using Remotion.Linq.Clauses.StreamedData;

namespace Marten.Linq
{
    public class StatsResultOperator
        : SequenceTypePreservingResultOperatorBase
    {
        public LambdaExpression Stats { get; set; }

        public StatsResultOperator(Expression parameter)
        {
            Parameter = parameter;
        }

        public StatsResultOperator(LambdaExpression stats)
        {
            Stats = stats;
        }

        public Expression Parameter { get; private set; }

        public override ResultOperatorBase Clone(CloneContext cloneContext)
        {
            return new StatsResultOperator(Parameter);
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