using System;
using Marten.Generation;

namespace Marten.Schema
{
    public static class DocumentSchemaExtensions
    {
        public static TableDefinition TableSchema(this DocumentStore store, Type documentType)
        {
            var mapping = store.DefaultTenant.MappingFor(documentType);
            return store.Schema.DbObjects.TableSchema(mapping);
        }
    }
}