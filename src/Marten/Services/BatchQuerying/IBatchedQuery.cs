using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Services.BatchQuerying
{
    public interface IBatchedQuery
    {
        Task<T> Load<T>(string id) where T : class;
        Task<T> Load<T>(ValueType id) where T : class;

        IBatchLoadByKeys<TDoc> LoadMany<TDoc>() where TDoc : class;


        Task<IEnumerable<T>> Query<T>(string sql, params object[] parameters) where T : class;

        Task Execute(CancellationToken token = default(CancellationToken));

        Task<bool> Any<TDoc>(Func<IQueryable<TDoc>, IQueryable<TDoc>> query);
        Task<bool> Any<TDoc>();

        Task<long> Count<TDoc>(Func<IQueryable<TDoc>, IQueryable<TDoc>> query);
        Task<long> Count<TDoc>();
    }
}