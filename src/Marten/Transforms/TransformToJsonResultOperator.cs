using System;
using System.Linq.Expressions;
using Baseline;
using Marten.Linq;
using Marten.Schema;
using Marten.Storage;
using Remotion.Linq.Clauses;
using Remotion.Linq.Clauses.ResultOperators;
using Remotion.Linq.Clauses.StreamedData;

namespace Marten.Transforms
{
    public class TransformToJsonResultOperator : SequenceTypePreservingResultOperatorBase, ISelectableOperator
    {
        private readonly string _transformName;

        public TransformToJsonResultOperator(string transformName)
        {
            _transformName = transformName;
        }

        public override ResultOperatorBase Clone(CloneContext cloneContext)
        {
            return new TransformToJsonResultOperator(_transformName);
        }

        public override void TransformExpressions(Func<Expression, Expression> transformation)
        {
            // no-op;
        }

        public override StreamedSequence ExecuteInMemory<T>(StreamedSequence input)
        {
            return input;
        }

        public ISelector<T> BuildSelector<T>(string dataLocator, ITenant schema, IQueryableDocument document)
        {
            var transform = schema.TransformFor(_transformName);
            return new TransformToJsonSelector(dataLocator, transform, document).As<ISelector<T>>();
        }
    }
}