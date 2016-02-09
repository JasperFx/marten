using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Marten.Services.BatchQuerying
{
    public interface IQueryForExpression<TDoc>
    {
        Task<TDoc> First(Func<IQueryable<TDoc>, IQueryable<TDoc>> query);
        Task<TDoc> FirstOrDefault(Func<IQueryable<TDoc>, IQueryable<TDoc>> query);

        Task<TDoc> Single(Func<IQueryable<TDoc>, IQueryable<TDoc>> query);
        Task<TDoc> SingleOrDefault(Func<IQueryable<TDoc>, IQueryable<TDoc>> query);

        Task<bool> Any(Func<IQueryable<TDoc>, IQueryable<TDoc>> query);
        Task<long> Count(Func<IQueryable<TDoc>, IQueryable<TDoc>> query);

        Task<IList<TDoc>> Query(Func<IQueryable<TDoc>, IQueryable<TDoc>> query);

    }


}