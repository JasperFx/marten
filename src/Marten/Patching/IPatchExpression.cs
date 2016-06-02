using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace Marten.Patching
{
    public interface IPatchExpression<T>
    {
        void Set<TValue>(Expression<Func<T, TValue>> expression, TValue value);
        void Increment(Expression<Func<T, int>> expression, int increment = 1);
        void Increment(Expression<Func<T, long>> expression, long increment = 1);
        void Increment(Expression<Func<T, double>> expression, double increment = 1);
        void Increment(Expression<Func<T, float>> expression, float increment = 1);
        void Append<TElement>(Expression<Func<T, IEnumerable<TElement>>> expression, TElement element);
        void Insert<TElement>(Expression<Func<T, IEnumerable<TElement>>> expression, TElement element, int index = 0);
        void Rename(string oldName, Expression<Func<T, object>> expression);
    }
}