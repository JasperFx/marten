using System.Collections.Generic;
using System.Threading.Tasks;

namespace Marten.Services.BatchQuerying
{
    public interface IBatchLoadByKeys<TDoc> where TDoc : class
    {
        Task<IReadOnlyList<TDoc>> ById<TKey>(params TKey[] keys);

        Task<IReadOnlyList<TDoc>> ByIdList<TKey>(IEnumerable<TKey> keys);
    }
}
