using System.Collections.Generic;
using System.Threading.Tasks;
#nullable enable
namespace Marten.Services.BatchQuerying
{
    public interface IBatchLoadByKeys<TDoc> where TDoc : class
    {
        /// <summary>
        /// Load multiple documents by an array of keys
        /// </summary>
        /// <param name="keys"></param>
        /// <typeparam name="TKey"></typeparam>
        /// <returns></returns>
        Task<IReadOnlyList<TDoc>> ById<TKey>(params TKey[] keys);

        /// <summary>
        /// Load multiple documents by a list of keys
        /// </summary>
        /// <param name="keys"></param>
        /// <typeparam name="TKey"></typeparam>
        /// <returns></returns>
        Task<IReadOnlyList<TDoc>> ByIdList<TKey>(IEnumerable<TKey> keys);
    }
}
