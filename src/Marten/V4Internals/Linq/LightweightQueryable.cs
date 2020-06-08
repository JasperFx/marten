using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace Marten.V4Internals.Linq
{
    public class LightweightQueryable<T> : IOrderedQueryable<T>, IQueryProvider
    {
        public LightweightQueryable(IMartenSession session)
        {
            Expression = Expression.Constant(this);
        }

        public IEnumerator<T> GetEnumerator()
        {
            // TODO -- execute ToList<T>() and return the enumerator for that
            throw new NotImplementedException();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public Expression Expression { get; protected set; }
        public Type ElementType => typeof (T);
        public IQueryProvider Provider => this;

        IQueryable IQueryProvider.CreateQuery(Expression expression)
        {
            // TODO -- this is necessary to create a new child expression.

            // *THINK* we do that by building a new selector/statement, then letting
            // the selector build the statement so it isn't done by reflection
            throw new NotImplementedException();
        }

        IQueryable<TElement> IQueryProvider.CreateQuery<TElement>(Expression expression)
        {
            // Create a new Queryable instance
            throw new NotImplementedException();
        }

        object IQueryProvider.Execute(Expression expression)
        {
            // build a QueryModel, turn that into a Statement

            // build it up, baby!
            throw new NotImplementedException();
        }

        TResult IQueryProvider.Execute<TResult>(Expression expression)
        {
            // build it up, baby!
            throw new NotImplementedException();
        }
    }
}
