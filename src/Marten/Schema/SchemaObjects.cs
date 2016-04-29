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

        public bool HasNone()
        {
            return Table == null;
        }
    }

    public class SchemaDiff
    {
        public SchemaDiff(IDocumentSchema schema, SchemaObjects existing, DocumentMapping mapping)
        {
            if (existing.HasNone())
            {
                AllMissing = true;
            }
            else
            {
                TableDiff = new TableDiff(mapping.ToTable(schema), existing.Table);
            }
        }

        public bool HasDifferences()
        {
            // TODO -- need to check indices and functions too
            return AllMissing || !TableDiff.Matches;
        }

        public TableDiff TableDiff { get; }

        public bool AllMissing { get; }
    }
}