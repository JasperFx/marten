using System;
using System.IO;
using Baseline;
using Marten.Generation;
using Marten.Util;

namespace Marten.Schema
{
    public class SchemaDiff
    {
        private readonly DocumentMapping _mapping;
        private readonly IDocumentSchema _schema;
        private readonly SchemaObjects _existing;

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

            _existing = existing;
            _mapping = mapping;
            _schema = schema;
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

        public bool HasFunctionChanged()
        {
            var writer = new StringWriter();
            
            _mapping.ToUpsertFunction().WriteFunctionSql(_schema.StoreOptions.UpsertType, writer);
            var expected = writer.ToString().CanonicizeSql();

            return !expected.Equals(_existing.UpsertFunction, StringComparison.OrdinalIgnoreCase);
        }


        public TableDiff TableDiff { get; }

        public bool AllMissing { get; }

        public void CreatePatch(Action<string> executeSql)
        {
            TableDiff.CreatePatch(_mapping, executeSql);
        }


    }
}