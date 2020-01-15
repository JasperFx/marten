using System;
using System.Collections.Generic;
using System.Linq;
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

            compareForeignKeys(expected, actual);
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

        private void compareForeignKeys(Table expected, Table actual)
        {
            var schemaName = expected.Identifier.Schema;
            var tableName = expected.Identifier.Name;

            // Locate FKs that exist, but aren't defined
            var obsoleteFkeys = actual.ActualForeignKeys.Where(afk => expected.ForeignKeys.All(fk => fk.KeyName != afk.Name));
            foreach (var fkey in obsoleteFkeys)
            {
                ForeignKeyMissing.Add($"ALTER TABLE {schemaName}.{tableName} DROP CONSTRAINT {fkey.Name};");
                ForeignKeyMissingRollbacks.Add($"ALTER TABLE {schemaName}.{tableName} ADD CONSTRAINT {fkey.Name} {fkey.DDL};");
            }

            // Detect changes
            foreach (var fkey in expected.ForeignKeys)
            {
                var actualFkey = actual.ActualForeignKeys.SingleOrDefault(afk => afk.Name == fkey.KeyName);
                if (actualFkey != null && fkey.CascadeDeletes != actualFkey.DoesCascadeDeletes())
                {
                    // The fkey cascading has changed, drop and re-create the key
                    ForeignKeyChanges.Add($"ALTER TABLE {schemaName}.{tableName} DROP CONSTRAINT {actualFkey.Name}; {fkey.ToDDL()};");
                    ForeignKeyRollbacks.Add($"ALTER TABLE {schemaName}.{tableName} DROP CONSTRAINT {fkey.KeyName}; ALTER TABLE {schemaName}.{tableName} ADD CONSTRAINT {actualFkey.Name} {actualFkey.DDL};");
                }
                else if (actualFkey == null)// The foreign key is missing
                {
                    ForeignKeyChanges.Add(fkey.ToDDL());
                    ForeignKeyRollbacks.Add($"ALTER TABLE {schemaName}.{tableName} DROP CONSTRAINT {fkey.KeyName};");
                }
            }
        }

        public readonly IList<string> IndexChanges = new List<string>();
        public readonly IList<string> IndexRollbacks = new List<string>();

        public readonly IList<string> ForeignKeyMissing = new List<string>();
        public readonly IList<string> ForeignKeyMissingRollbacks = new List<string>();

        public readonly IList<string> ForeignKeyChanges = new List<string>();
        public readonly IList<string> ForeignKeyRollbacks = new List<string>();

        public TableColumn[] Different { get; set; }

        public TableColumn[] Matched { get; set; }

        public TableColumn[] Extras { get; set; }

        public TableColumn[] Missing { get; set; }

        public bool Matches
        {
            get
            {
                if (Missing.Any())
                    return false;
                if (Extras.Any())
                    return false;
                if (Different.Any())
                    return false;
                if (IndexChanges.Any())
                    return false;
                if (ForeignKeyChanges.Any())
                    return false;

                return true;
            }
        }

        public override string ToString()
        {
            return $"TableDiff for {_tableName}";
        }
    }
}