using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Services
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

    public interface IQueryForExpression<TDoc>
    {
        Task<TReturn> For<TReturn>(Func<IQueryable<TDoc>, TReturn> query);
    }

    public interface IBatchLoadByKeys<TDoc>
    {
        Task<IEnumerable<TDoc>> ById<TKey>(params TKey[] keys);

        Task<IEnumerable<TDoc>> ById<TKey>(IEnumerable<TKey> keys);
    }
}