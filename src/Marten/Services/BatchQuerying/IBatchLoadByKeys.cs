using System.Collections.Generic;
using System.Threading.Tasks;

namespace Marten.Services.BatchQuerying
{
    public interface IBatchLoadByKeys<TDoc> where TDoc : class
    {
        Task<IList<TDoc>> ById<TKey>(params TKey[] keys) ;

        Task<IList<TDoc>> ByIdList<TKey>(IEnumerable<TKey> keys);
    }
}