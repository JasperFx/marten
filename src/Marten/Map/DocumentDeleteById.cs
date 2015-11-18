using System;
using Marten.Schema;
using Npgsql;

namespace Marten.Map
{
    public class DocumentDeleteById : DocumentChange
    {
        private readonly Type _documentType;
        private readonly object _id;

        public DocumentDeleteById(Type documentType, object id)
        {
            _documentType = documentType;
            _id = id;
        }

        public override NpgsqlCommand CreateCommand(IDocumentSchema schema)
        {
            var storage = schema.StorageFor(_documentType);
            return storage.DeleteCommandForId(_id);
        }
    }
}