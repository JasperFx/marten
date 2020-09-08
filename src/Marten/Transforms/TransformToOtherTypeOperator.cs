using System;
using System.Linq.Expressions;
using Marten.Internal;
using Marten.Linq.SqlGeneration;
using Remotion.Linq.Clauses;
using Remotion.Linq.Clauses.ResultOperators;
using Remotion.Linq.Clauses.StreamedData;

namespace Marten.Transforms
{
    public class TransformToOtherTypeOperator<T> : SequenceTypePreservingResultOperatorBase, ISelectableOperator
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


        public SelectorStatement ModifyStatement(SelectorStatement statement, IMartenSession session)
        {
            var transform = session.Tenant.TransformFor(_transformName);

            var clause = new DataSelectClause<T>(statement.SelectClause.FromObject)
            {
                FieldName = $"{transform.Identifier}(d.data)"
            };

            statement.SelectClause = clause;

            return statement;
        }
    }
}
