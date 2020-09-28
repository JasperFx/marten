using System;
using Marten.Storage;

namespace Marten.Schema.Testing
{
    public static class DocumentSchemaExtensions
    {
        internal static DocumentTable TableSchema(this DocumentStore store, Type documentType)
        {
            var mapping = store.Tenancy.Default.MappingFor(documentType);
            return new DocumentTable((DocumentMapping)mapping);
        }
    }
}
