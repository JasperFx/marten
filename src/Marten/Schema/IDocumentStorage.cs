using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Linq;
using Marten.Services;
using Marten.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Marten.Schema
{
    public interface IDocumentStorage
    {
        TenancyStyle TenancyStyle { get; }

        Type DocumentType { get; }

        NpgsqlDbType IdType { get; }
        NpgsqlCommand LoaderCommand(object id);

        NpgsqlCommand LoadByArrayCommand<TKey>(TKey[] ids);

        object Identity(object document);

        void Remove(IIdentityMap map, object entity);
        
        void Delete(IIdentityMap map, object id);

        void Store(IIdentityMap map, object id, object entity);

        IStorageOperation DeletionForId(TenancyStyle tenancyStyle, object id);
        IStorageOperation DeletionForEntity(TenancyStyle tenancyStyle, object entity);

        IStorageOperation DeletionForWhere(IWhereFragment @where, TenancyStyle tenancyStyle);

        void RegisterUpdate(UpdateBatch batch, object entity);
        void RegisterUpdate(UpdateBatch batch, object entity, string json);
    }

    public interface IDocumentStorage<T> : IDocumentStorage
    {
        // Gets run through the identity map to do most of the actual work
        T Resolve(int startingIndex, DbDataReader reader, IIdentityMap map);
        Task<T> ResolveAsync(int startingIndex, DbDataReader reader, IIdentityMap map, CancellationToken token);

        // Goes through the IdentityMap to do its thing
        T Resolve(IIdentityMap map, ILoader loader, object id);
        Task<T> ResolveAsync(IIdentityMap map, ILoader loader, CancellationToken token, object id);

        // Used to load by id
        FetchResult<T> Fetch(DbDataReader reader, ISerializer serializer);


        Task<FetchResult<T>> FetchAsync(DbDataReader reader, ISerializer serializer, CancellationToken token);
    }

}