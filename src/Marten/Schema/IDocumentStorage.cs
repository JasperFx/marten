using Marten.Generation;
using Npgsql;

namespace Marten.Schema
{
    public interface IDocumentStorage
    {
        void InitializeSchema(SchemaBuilder builder);
        NpgsqlCommand UpsertCommand(object document, string json);
        NpgsqlCommand LoaderCommand(object id);
        NpgsqlCommand DeleteCommand(object id);
    }

    // TODO -- might kill off this thing later
    public interface IDocumentStorage<T> : IDocumentStorage where T : IDocument
    {
        // Later
        //DataTable CreateTable();
        //NpgsqlCommand UpsertCommand();

        NpgsqlCommand UpsertCommand(T document, string json);
        


    }
}