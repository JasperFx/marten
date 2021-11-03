using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Remotion.Linq;
using Remotion.Linq.Clauses;

namespace Marten.Linq.Operators
{
    internal class OrderByComparerClause: IBodyClause
    {
        private OrderByComparerClause(bool caseInsensitive)
        {
            CaseInsensitive = caseInsensitive;
        }

        public OrderByComparerClause(bool caseInsensitive, Ordering ordering)
        {
            CaseInsensitive = caseInsensitive;
            Orderings.Add(ordering);
        }

        public bool CaseInsensitive { get; }
        public List<Ordering> Orderings { get; } = new List<Ordering>();

        public void TransformExpressions(Func<Expression, Expression> transformation) => throw new NotImplementedException();

        public void Accept(IQueryModelVisitor visitor, QueryModel queryModel, int index) => throw new NotImplementedException();

        public IBodyClause Clone(CloneContext cloneContext)
        {
            var clone = new OrderByComparerClause(CaseInsensitive);

            foreach (var ordering in Orderings)
            {
                var clonedOrdering = ordering.Clone(cloneContext);
                clone.Orderings.Add(clonedOrdering);
            }

            return clone;
        }
    }
}
