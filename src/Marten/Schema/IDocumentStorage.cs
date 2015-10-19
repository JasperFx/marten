using Marten.Generation;
using Npgsql;

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
    }

}