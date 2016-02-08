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
        //void Remove(IIdentityMap map, object entity);
        // _identityMap.Remove<T>(_schema.StorageFor(typeof(T)).Identity(entity));

        //void Delete(IIdentityMap map, object id);
        // _identityMap.Remove<T>(id);

        //void Store(IIdentityMap map, object id, object entity);
        /*
                if (_identityMap.Has<T>(id))
                {
                    var existing = _identityMap.Retrieve<T>(id);
                    if (!ReferenceEquals(existing, entity))
                    {
                        throw new InvalidOperationException(
                            $"Document '{typeof(T).FullName}' with same Id already added to the session.");
                    }
                }
                else
                {
                    _identityMap.Store(id, entity);
                }

    */
    }

}