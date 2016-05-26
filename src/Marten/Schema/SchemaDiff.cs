using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
                var expectedTable = mapping.SchemaObjects.As<DocumentSchemaObjects>().StorageTable();
                TableDiff = new TableDiff(expectedTable, existing.Table);

                // TODO -- drop obsolete indices?

                mapping.Indexes.Each(index =>
                {
                    if (existing.ActualIndices.ContainsKey(index.IndexName))
                    {
                        var actualIndex = existing.ActualIndices[index.IndexName];
                        if (!index.Matches(actualIndex))
                        {
                            IndexChanges.Add($"drop index {expectedTable.Table.Schema}.{index.IndexName};{index.ToDDL()}");
                        }
                    }
                    else
                    {
                        IndexChanges.Add(index.ToDDL());
                    }
                });

            }

            _existing = existing;
            _mapping = mapping;
            _schema = schema;


        }

        public bool HasDifferences()
        {
            if (AllMissing) return true;
            if (!TableDiff.Matches) return true;
            if (HasFunctionChanged()) return true;

            return IndexChanges.Any();
        }

        public bool CanPatch()
        {
            return TableDiff.CanPatch();
        }

        private string expectedUpsertFunction()
        {
            var writer = new StringWriter();

            new UpsertFunction(_mapping).WriteFunctionSql(writer);

            return writer.ToString();
        }

        public bool HasFunctionChanged()
        {
            if (_existing.UpsertFunction.IsEmpty()) return true;

            var expected = expectedUpsertFunction().CanonicizeSql();

            return !expected.Equals(_existing.UpsertFunction, StringComparison.OrdinalIgnoreCase);
        }


        public TableDiff TableDiff { get; }

        public bool AllMissing { get; }

        public readonly IList<string> IndexChanges = new List<string>();

        public void CreatePatch(Action<string> executeSql)
        {
            TableDiff.CreatePatch(_mapping, executeSql);

            if (HasFunctionChanged())
            {
                _existing.FunctionDropStatements.Each(executeSql);

                // TODO -- need to drop the existing function somehow?
                executeSql(expectedUpsertFunction());
            }

            IndexChanges.Each(executeSql);
        }

        
    }
}