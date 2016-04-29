using System;
using Marten.Generation;

namespace Marten.Schema
{
    public class SchemaObjects
    {
        public Type DocumentType { get; }
        public TableDefinition Table { get; }
        public IndexDef[] Indices { get; }
        public string UpsertFunction { get; }

        public SchemaObjects(Type documentType, TableDefinition table, IndexDef[] indices, string upsertFunction)
        {
            DocumentType = documentType;
            Table = table;
            Indices = indices;
            UpsertFunction = upsertFunction;
        }
    }
}