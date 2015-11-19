using Marten.Schema;
using Npgsql;

namespace Marten.Map
{
    public class DocumentUpdate : DocumentChange
    {

        public DocumentIdentity Id { get; }
        public string Json { get; }
        public object Document { get; }

        public DocumentUpdate(object document, string json, DocumentIdentity id)
        {
            Document = document;
            Json = json;
            Id = id;
        }

        public override NpgsqlCommand CreateCommand(IDocumentSchema schema)
        {
            var storage = schema.StorageFor(Document.GetType());
            return storage.UpsertCommand(Document, Json);
        }
    }
}