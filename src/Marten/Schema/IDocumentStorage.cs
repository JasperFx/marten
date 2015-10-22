using System;
using Marten.Generation;
using Npgsql;
using Remotion.Linq;

namespace Marten.Schema
{
    public interface IDocumentStorage
    {
        void InitializeSchema(SchemaBuilder builder);
        NpgsqlCommand UpsertCommand(object document, string json);
        NpgsqlCommand LoaderCommand(object id);
        NpgsqlCommand DeleteCommandForId(object id);
        NpgsqlCommand DeleteCommandForEntity(object entity);
        string TableName { get; }

        NpgsqlCommand LoadByArrayCommand<TKey>(TKey[] ids);
        NpgsqlCommand AnyCommand(QueryModel queryModel);
    }

}