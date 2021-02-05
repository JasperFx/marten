using System;
using Marten.Storage;

namespace Marten.Schema.Testing
{
    public static class DocumentSchemaExtensions
    {
        internal static DocumentTable TableSchema(this DocumentStore store, Type documentType)
        {
            var mapping = store.Storage.MappingFor(documentType);
            return new DocumentTable(mapping);
        }
    }
}
