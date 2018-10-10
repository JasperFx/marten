using System;
using System.Collections.Generic;
using System.Linq;
using Baseline;
using Marten.Schema;

namespace Marten.Storage
{
    public class TableDelta
    {
        private readonly DbObjectName _tableName;

        public TableDelta(Table expected, Table actual)
        {
            Missing = expected.Where(x => actual.All(_ => _.Name != x.Name)).ToArray();
            Extras = actual.Where(x => expected.All(_ => _.Name != x.Name)).ToArray();
            Matched = expected.Where(x => actual.Any(a => Equals(a, x))).ToArray();
            Different =
                expected.Where(x => actual.HasColumn(x.Name) && !x.Equals(actual.ColumnFor(x.Name))).ToArray();

            _tableName = expected.Identifier;

            compareIndices(expected, actual);

            var missingFKs = expected.ForeignKeys.Where(x => !actual.ActualForeignKeys.Contains(x.KeyName));
            MissingForeignKeys.AddRange(missingFKs);
        }

        private void compareIndices(Table expected, Table actual)
        {
            // TODO -- drop obsolete indices?

            var schemaName = expected.Identifier.Schema;

            var obsoleteIndexes = actual.ActualIndices.Values.Where(x => expected.Indexes.All(_ => _.IndexName != x.Name));
            foreach (var index in obsoleteIndexes)
            {
                IndexRollbacks.Add(index.DDL);

                if (!index.Name.EndsWith("pkey"))
                {
                    IndexChanges.Add($"drop index concurrently if exists {schemaName}.{index.Name};");
                }
                /*                else
                                {
                                    IndexChanges.Add($"alter table {_tableName} drop constraint if exists {schemaName}.{index.Name};");
                                }*/
            }

            foreach (var index in expected.Indexes)
            {
                if (actual.ActualIndices.ContainsKey(index.IndexName))
                {
                    var actualIndex = actual.ActualIndices[index.IndexName];
                    if (!index.Matches(actualIndex))
                    {
                        IndexChanges.Add($"drop index {schemaName}.{index.IndexName};{Environment.NewLine}{index.ToDDL()};");
                        IndexRollbacks.Add($"drop index {schemaName}.{index.IndexName};{Environment.NewLine}{actualIndex.DDL};");
                    }
                }
                else
                {
                    IndexChanges.Add(index.ToDDL());
                    IndexRollbacks.Add($"drop index concurrently if exists {schemaName}.{index.IndexName};");
                }
            }
        }

        public readonly IList<string> IndexChanges = new List<string>();
        public readonly IList<string> IndexRollbacks = new List<string>();

        public TableColumn[] Different { get; set; }

        public TableColumn[] Matched { get; set; }

        public TableColumn[] Extras { get; set; }

        public TableColumn[] Missing { get; set; }

        public IList<ForeignKeyDefinition> MissingForeignKeys { get; } = new List<ForeignKeyDefinition>();

        public bool Matches
        {
            get
            {
                if (Missing.Any()) return false;
                if (Extras.Any()) return false;
                if (Different.Any()) return false;
                if (IndexChanges.Any()) return false;

                return true;
            }
        }

        public override string ToString()
        {
            return $"TableDiff for {_tableName}";
        }
    }
}