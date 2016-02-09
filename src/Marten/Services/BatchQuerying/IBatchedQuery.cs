using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Marten.Services.BatchQuerying
{
    public interface IBatchedQuery
    {
        Task<T> Load<T>(string id);
        Task<T> Load<T>(ValueType id);

        IBatchLoadByKeys<TDoc> Load<TDoc>();


        Task<IEnumerable<T>> Query<T>(string sql, params object[] parameters);


        IQueryForExpression<T> Query<T>();


        Task Execute();

    }
}