using System;
using System.Linq.Expressions;
using Baseline;
using Marten.Internal;
using Marten.Linq;
using Marten.Linq.SqlGeneration;
using Marten.Schema;
using Marten.Storage;
using Remotion.Linq.Clauses;
using Remotion.Linq.Clauses.ResultOperators;
using Remotion.Linq.Clauses.StreamedData;

namespace Marten.Transforms
{
    internal class TransformToJsonResultOperator: SequenceTypePreservingResultOperatorBase, ISelectableOperator
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

        public SelectorStatement ModifyStatement(SelectorStatement statement, IMartenSession session)
        {
            var transform = session.Tenant.TransformFor(_transformName);

            var clause = new JsonSelectClause(statement.SelectClause)
            {
                SelectionText = $"select {transform.Identifier}(d.data) from "
            };

            statement.SelectClause = clause;

            return statement;
        }
    }
}
