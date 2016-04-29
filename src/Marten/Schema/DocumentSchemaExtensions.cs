using System;
using Marten.Generation;

namespace Marten.Schema
{
    public static class DocumentSchemaExtensions
    {
        public static TableDefinition TableSchema(this IDocumentSchema schema, Type documentType)
        {
            var mapping = schema.MappingFor(documentType);
            return schema.DbObjects.TableSchema(mapping);
        }
    }
}