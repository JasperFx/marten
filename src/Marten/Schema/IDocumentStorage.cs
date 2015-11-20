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
        NpgsqlCommand UpsertCommand(object document, string json);
        NpgsqlCommand LoaderCommand(object id);
        NpgsqlCommand DeleteCommandForId(object id);
        NpgsqlCommand DeleteCommandForEntity(object entity);
        NpgsqlCommand LoadByArrayCommand<TKey>(TKey[] ids);

        object Identity(object document);


        void RegisterUpdate(UpdateBatch batch, object entity);
        void RegisterUpdate(UpdateBatch batch, object entity, string json);
    }
}