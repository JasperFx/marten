using System;
using Marten.Services;
using Npgsql;
using NpgsqlTypes;

namespace Marten.Schema
{
    public interface IDocumentStorage
    {
        Type DocumentType { get; }

        NpgsqlDbType IdType { get; }
        NpgsqlCommand LoaderCommand(object id);
        NpgsqlCommand DeleteCommandForId(object id);
        NpgsqlCommand DeleteCommandForEntity(object entity);
        NpgsqlCommand LoadByArrayCommand<TKey>(TKey[] ids);

        object Identity(object document);

        void RegisterUpdate(UpdateBatch batch, object entity);
        void RegisterUpdate(UpdateBatch batch, object entity, string json);


        // NEW METHODS!
        void Remove(IIdentityMap map, object entity);
        // _identityMap.Remove<T>(_schema.StorageFor(typeof(T)).Identity(entity));

        void Delete(IIdentityMap map, object id);
        // _identityMap.Remove<T>(id);

        void Store(IIdentityMap map, object id, object entity);
        //  _identityMap.Store(id, entity);

    }

}