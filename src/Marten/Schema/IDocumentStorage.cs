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


    public enum UpdateStyle
    {
        Upsert,
        Insert,
        Update
    }

    public interface IDocumentStorage
    {
        TenancyStyle TenancyStyle { get; }

        Type DocumentType { get; }

        Type TopLevelBaseType { get; }

        NpgsqlDbType IdType { get; }
        NpgsqlCommand LoaderCommand(object id);

        NpgsqlCommand LoadByArrayCommand<TKey>(TKey[] ids);

        object Identity(object document);

        void Remove(IIdentityMap map, object entity);
        
        void Delete(IIdentityMap map, object id);

        void Store(IIdentityMap map, object id, object entity);

        IStorageOperation DeletionForId(object id);
        IStorageOperation DeletionForEntity(object entity);

        IStorageOperation DeletionForWhere(IWhereFragment @where);

        void RegisterUpdate(string tenantIdOverride, UpdateStyle updateStyle, UpdateBatch batch, object entity);
        void RegisterUpdate(string tenantIdOverride, UpdateStyle updateStyle, UpdateBatch batch, object entity, string json);
    }

    public interface IDocumentStorage<T> : IDocumentStorage
    {
        // Gets run through the identity map to do most of the actual work
        T Resolve(int startingIndex, DbDataReader reader, IIdentityMap map);
        Task<T> ResolveAsync(int startingIndex, DbDataReader reader, IIdentityMap map, CancellationToken token);

        // Goes through the IdentityMap to do its thing
        T Resolve(IIdentityMap map, IQuerySession session, object id);
        Task<T> ResolveAsync(IIdentityMap map, IQuerySession session, CancellationToken token, object id);
    }

}