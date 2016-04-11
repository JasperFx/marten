using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Marten.Services.Includes;

namespace Marten.Linq
{
    public interface IMartenQueryable<T> : IQueryable<T>
    {

        Task<IList<T>> ExecuteCollectionAsync(CancellationToken token);
        Task<IEnumerable<string>> ExecuteCollectionToJsonAsync(CancellationToken token);
        IEnumerable<string> ExecuteCollectionToJson();
        QueryPlan Explain();
        IMartenQueryable<T> Include<TInclude>(Expression<Func<T, object>> idSource, Action<TInclude> callback, JoinType joinType = JoinType.Inner) where TInclude : class;
        IMartenQueryable<T> Include<TInclude>(Expression<Func<T, object>> idSource, IList<TInclude> list, JoinType joinType = JoinType.Inner) where TInclude : class;
        IMartenQueryable<T> Include<TInclude, TKey>(Expression<Func<T, object>> idSource, IDictionary<TKey, TInclude> dictionary, JoinType joinType = JoinType.Inner) where TInclude : class;



        IEnumerable<IIncludeJoin> Includes { get; } 
    }
}