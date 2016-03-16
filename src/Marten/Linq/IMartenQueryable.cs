using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Linq
{
    public interface IMartenQueryable<T> : IQueryable<T>
    {
        Task<IEnumerable<T>> ExecuteCollectionAsync(CancellationToken token);
        IMartenQueryable<T> Include<TInclude>(Expression<Func<T, object>> idSource, Action<TInclude> callback) where TInclude : class;
    }
}