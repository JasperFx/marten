using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Services;

namespace Marten.Schema
{
    [Obsolete("Try to remove this, but move more of the logic to IDocumentStorage as [Method]<T> instead of Resolver<T>")]
    public interface IResolver<T>
    {
        // Gets run through the identity map to do most of the actual work
        T Resolve(int startingIndex, DbDataReader reader, IIdentityMap map);
        Task<T> ResolveAsync(int startingIndex, DbDataReader reader, IIdentityMap map, CancellationToken token);

        // Goes through the IdentityMap to do its thing
        T Resolve(IIdentityMap map, ILoader loader, object id);
        Task<T> ResolveAsync(IIdentityMap map, ILoader loader, CancellationToken token, object id);

        object Identity(object document);

        // Used to load by id
        FetchResult<T> Fetch(DbDataReader reader, ISerializer serializer);

        
        Task<FetchResult<T>> FetchAsync(DbDataReader reader, ISerializer serializer, CancellationToken token);
    }
}