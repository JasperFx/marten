using Marten.Schema;
using Npgsql;

namespace Marten.Map
{
    public class DocumentDelete : DocumentChange
    {
        private readonly object _document;

        public DocumentDelete(object document)
        {
            _document = document;
        }

        public override NpgsqlCommand CreateCommand(IDocumentSchema schema)
        {
            var storage = schema.StorageFor(_document.GetType());
            return storage.DeleteCommandForEntity(_document);
        }
    }
}