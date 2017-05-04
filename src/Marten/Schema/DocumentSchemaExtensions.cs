using System;
using Marten.Storage;

namespace Marten.Schema
{
    public static class DocumentSchemaExtensions
    {
        public static DocumentTable TableSchema(this DocumentStore store, Type documentType)
        {
            var mapping = store.Tenants.Default.MappingFor(documentType);
            return new DocumentTable((DocumentMapping) mapping);
        }
    }
}