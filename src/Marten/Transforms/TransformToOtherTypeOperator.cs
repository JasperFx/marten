using System;
using System.Linq.Expressions;
using Marten.Linq;
using Marten.Schema;
using Marten.Storage;
using Remotion.Linq.Clauses;
using Remotion.Linq.Clauses.ResultOperators;
using Remotion.Linq.Clauses.StreamedData;

namespace Marten.Transforms
{
    public class TransformToOtherTypeOperator : SequenceTypePreservingResultOperatorBase, ISelectableOperator
    {
        private readonly string _transformName;

        public TransformToOtherTypeOperator(string transformName)
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

            return new TransformToTypeSelector<T>(dataLocator, transform, document);
        }
    }
}