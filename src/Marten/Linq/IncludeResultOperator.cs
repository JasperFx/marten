using System;
using System.Linq.Expressions;
using Remotion.Linq.Clauses;
using Remotion.Linq.Clauses.ResultOperators;
using Remotion.Linq.Clauses.StreamedData;

namespace Marten.Linq
{
    public class IncludeResultOperator
        : SequenceTypePreservingResultOperatorBase
    {
        public LambdaExpression IdSource { get; set; }
        public Expression Callback { get; set; }

        public IncludeResultOperator(Expression parameter)
        {
            Parameter = parameter;
        }

        public IncludeResultOperator(LambdaExpression idSource, Expression callback)
        {
            IdSource = idSource;
            Callback = callback;
        }

        public Expression Parameter { get; private set; }

        public override ResultOperatorBase Clone(CloneContext cloneContext)
        {
            return new IncludeResultOperator(Parameter);
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
