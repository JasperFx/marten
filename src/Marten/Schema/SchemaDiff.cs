using System;
using Marten.Generation;

namespace Marten.Schema
{
    public class SchemaDiff
    {
        private readonly DocumentMapping _mapping;

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

            _mapping = mapping;
        }

        public bool HasDifferences()
        {
            // TODO -- need to check indices and functions too
            return AllMissing || !TableDiff.Matches;
        }

        public bool CanPatch()
        {
            // TODO -- need to check indices and functions too
            return TableDiff.CanPatch();
        }


        public TableDiff TableDiff { get; }

        public bool AllMissing { get; }

        public void CreatePatch(Action<string> executeSql)
        {
            TableDiff.CreatePatch(_mapping, executeSql);
        }


    }
}