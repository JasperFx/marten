using System.Collections.Generic;
using System.Threading.Tasks;

namespace Marten.Services.BatchQuerying
{
    public interface IBatchLoadByKeys<TDoc>
    {
        Task<IEnumerable<TDoc>> ById<TKey>(params TKey[] keys);

        Task<IEnumerable<TDoc>> ById<TKey>(IEnumerable<TKey> keys);
    }
}