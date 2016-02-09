using System;
using System.Collections.Generic;
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


        IQueryForExpression<T> Query<T>() where T : class;


        Task Execute(CancellationToken token = default(CancellationToken));

    }
}