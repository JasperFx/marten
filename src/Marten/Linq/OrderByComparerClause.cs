using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Remotion.Linq;
using Remotion.Linq.Clauses;

namespace Marten.Linq
{
    internal class OrderByComparerClause: IBodyClause
    {
        public OrderByComparerClause(bool caseSensitive, Ordering ordering)
        {
            CaseSensitive = caseSensitive;
            Orderings.Add(ordering);
        }

        public bool CaseSensitive { get; }

        public readonly List<Ordering> Orderings = new List<Ordering>();

        public void TransformExpressions(Func<Expression, Expression> transformation) => throw new NotImplementedException();

        public void Accept(IQueryModelVisitor visitor, QueryModel queryModel, int index) => throw new NotImplementedException();

        public IBodyClause Clone(CloneContext cloneContext) => throw new NotImplementedException();
    }
}
