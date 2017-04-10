using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Baseline;
using Marten.Generation;
using Marten.Storage;

namespace Marten.Schema
{
    public class SchemaDiff
    {
        private readonly DocumentMapping _mapping;

        public SchemaDiff(SchemaObjects existing, DocumentMapping mapping, DdlRules rules)
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
                            IndexChanges.Add($"drop index {expectedTable.Name.Schema}.{index.IndexName};{Environment.NewLine}{index.ToDDL()};");
                            IndexRollbacks.Add($"drop index {expectedTable.Name.Schema}.{index.IndexName};{Environment.NewLine}{actualIndex.DDL};");
                        }
                    }
                    else
                    {
                        IndexChanges.Add(index.ToDDL());
                        IndexRollbacks.Add($"drop index concurrently if exists {expectedTable.Name.Schema}.{index.IndexName};");
                    }
                });

                existing.ActualIndices.Values.Where(x => mapping.Indexes.All(_ => _.IndexName != x.Name)).Each(
                    index =>
                    {
                        IndexRollbacks.Add(index.DDL);
                        IndexChanges.Add($"drop index concurrently if exists {mapping.Table.Schema}.{index.Name};");
                    });

                var expectedFunction = new UpsertFunction(mapping);

                FunctionDiff = new FunctionDiff(expectedFunction.ToBody(rules), existing.Function);

                var missingFKs = mapping.ForeignKeys.Where(x => !existing.ForeignKeys.Contains(x.KeyName));
                MissingForeignKeys.AddRange(missingFKs);
            }

            _mapping = mapping;


        }

        public IList<ForeignKeyDefinition> MissingForeignKeys { get; } = new List<ForeignKeyDefinition>();

        public FunctionDiff FunctionDiff { get; set; }

        public bool HasDifferences()
        {
            if (AllMissing) return true;
            if (!TableDiff.Matches) return true;
            if (FunctionDiff.HasChanged) return true;

            return IndexChanges.Any() || MissingForeignKeys.Any();
        }

        public bool CanPatch()
        {
            return AllMissing || TableDiff.CanPatch();
        }


        public TableDiff TableDiff { get; }

        public bool AllMissing { get; }

        public readonly IList<string> IndexChanges = new List<string>();
        public readonly IList<string> IndexRollbacks = new List<string>();

        public void CreatePatch(StoreOptions options, SchemaPatch patch)
        {
            TableDiff.CreatePatch(_mapping, patch);

            FunctionDiff.WritePatch(options, patch);

            IndexChanges.Each(x => patch.Updates.Apply(this, x));
            IndexRollbacks.Each(x => patch.Rollbacks.Apply(this, x));

            MissingForeignKeys.Each(x => patch.Updates.Apply(this, x.ToDDL()));
        }

        public override string ToString()
        {
            return $"SchemaDiff for {_mapping.DocumentType.FullName}";
        }
    }
}