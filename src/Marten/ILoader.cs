using System;
using System.Threading;
using System.Threading.Tasks;
using Marten.Services;

namespace Marten
{
    [Obsolete("try to eliminate this")]
    public interface ILoader
    {
        FetchResult<T> LoadDocument<T>(object id) where T : class;
        Task<FetchResult<T>> LoadDocumentAsync<T>(object id, CancellationToken token) where T : class;
    }
}