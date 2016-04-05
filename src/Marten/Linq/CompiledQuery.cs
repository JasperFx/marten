using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace Marten.Linq
{
    public interface ISingleItemCompiledQuery<TDoc, TOut>
    {
        Expression<Func<IQueryable<TDoc>, TOut>> QueryIs();
    }

    public interface IMultipleItemCompiledQuery<TDoc, TOut>
    {
        Expression<Func<IQueryable<TDoc>, IEnumerable<TOut>>> QueryIs();
    }
}