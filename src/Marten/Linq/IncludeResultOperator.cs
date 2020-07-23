using System;
using System.Linq.Expressions;
using Marten.Internal.Linq.Includes;
using Remotion.Linq.Clauses;
using Remotion.Linq.Clauses.ResultOperators;
using Remotion.Linq.Clauses.StreamedData;

namespace Marten.Linq
{
    public class IncludeResultOperator
        : SequenceTypePreservingResultOperatorBase
    {
        public IncludeResultOperator(IIncludePlan include)
        {
            Include = include;
        }

        public IIncludePlan Include { get; private set; }

        public override ResultOperatorBase Clone(CloneContext cloneContext)
        {
            return new IncludeResultOperator(Include);
        }

        public override void TransformExpressions(
            Func<Expression, Expression> transformation)
        {
            throw new NotImplementedException();
        }

        public override StreamedSequence ExecuteInMemory<T>(StreamedSequence input)
        {
            return input;
        }
    }
}
