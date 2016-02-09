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

        Task<TDoc> Any(Func<IQueryable<TDoc>, IQueryable<TDoc>> query);
        Task<long> Count(Func<IQueryable<TDoc>, IQueryable<TDoc>> query);

        Task<IList<TDoc>> Query(Func<IQueryable<TDoc>, IQueryable<TDoc>> query);

    }

    public class QueryForExpression<TDoc> : IQueryForExpression<TDoc>
    {
        public Task<TDoc> First(Func<IQueryable<TDoc>, IQueryable<TDoc>> query)
        {
            throw new NotImplementedException();
        }

        public Task<TDoc> FirstOrDefault(Func<IQueryable<TDoc>, IQueryable<TDoc>> query)
        {
            throw new NotImplementedException();
        }

        public Task<TDoc> Single(Func<IQueryable<TDoc>, IQueryable<TDoc>> query)
        {
            throw new NotImplementedException();
        }

        public Task<TDoc> SingleOrDefault(Func<IQueryable<TDoc>, IQueryable<TDoc>> query)
        {
            throw new NotImplementedException();
        }

        public Task<TDoc> Any(Func<IQueryable<TDoc>, IQueryable<TDoc>> query)
        {
            throw new NotImplementedException();
        }

        public Task<long> Count(Func<IQueryable<TDoc>, IQueryable<TDoc>> query)
        {
            throw new NotImplementedException();
        }

        public Task<IList<TDoc>> Query(Func<IQueryable<TDoc>, IQueryable<TDoc>> query)
        {
            throw new NotImplementedException();
        }
    }
}