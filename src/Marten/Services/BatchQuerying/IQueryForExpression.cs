using System;
using System.Linq;
using System.Threading.Tasks;

namespace Marten.Services.BatchQuerying
{
    public interface IQueryForExpression<TDoc>
    {
        Task<TReturn> For<TReturn>(Func<IQueryable<TDoc>, TReturn> query);
    }
}