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
        private readonly SchemaObjects _existing;

        public SchemaDiff(SchemaObjects existing, DocumentMapping mapping)
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
                            IndexChanges.Add($"drop index {expectedTable.Table.Schema}.{index.IndexName};{Environment.NewLine}{index.ToDDL()};");
                            IndexRollbacks.Add($"drop index {expectedTable.Table.Schema}.{index.IndexName};{Environment.NewLine}{actualIndex.DDL};");
                        }
                    }
                    else
                    {
                        IndexChanges.Add(index.ToDDL());
                        IndexRollbacks.Add($"drop index concurrently if exists {expectedTable.Table.Schema}.{index.IndexName} cascade;");
                    }
                });

                existing.ActualIndices.Values.Where(x => !mapping.Indexes.Any(_ => _.IndexName == x.Name)).Each(
                    index =>
                    {
                        IndexRollbacks.Add(index.DDL);
                        IndexChanges.Add($"drop index concurrently if exists {mapping.Table.Schema}.{index.Name} cascade;");
                    });

                var expectedFunction = new UpsertFunction(mapping);

                FunctionDiff = new FunctionDiff(expectedFunction.ToBody(), existing.Function);
            }

            _existing = existing;
            _mapping = mapping;


        }

        public FunctionDiff FunctionDiff { get; set; }

        public bool HasDifferences()
        {
            if (AllMissing) return true;
            if (!TableDiff.Matches) return true;
            if (FunctionDiff.HasChanged) return true;

            return IndexChanges.Any();
        }

        public bool CanPatch()
        {
            return AllMissing || TableDiff.CanPatch();
        }


        public TableDiff TableDiff { get; }

        public bool AllMissing { get; }

        public readonly IList<string> IndexChanges = new List<string>();
        public readonly IList<string> IndexRollbacks = new List<string>();

        public void CreatePatch(SchemaPatch patch)
        {
            TableDiff.CreatePatch(_mapping, patch);

            FunctionDiff.WritePatch(patch);

            IndexChanges.Each(x => patch.Updates.Apply(this, x));
            IndexRollbacks.Each(x => patch.Rollbacks.Apply(this, x));
        }

        public override string ToString()
        {
            return $"SchemaDiff for {_mapping.DocumentType.FullName}";
        }
    }
}