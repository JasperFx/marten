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

        void Remove(IIdentityMap map, object entity);

        void Delete(IIdentityMap map, object id);

        void Store(IIdentityMap map, object id, object entity);

    }

}