using System;
using System.Linq;
using Baseline;
using Marten.Schema;

namespace Marten.Generation
{
    public class TableDiff
    {
        private readonly TableName _tableName;
        private TableDefinition _expected;

        public TableDiff(TableDefinition expected, TableDefinition actual)
        {
            Missing = expected.Columns.Where(x => actual.Columns.All(_ => _.Name != x.Name)).ToArray();
            Extras = actual.Columns.Where(x => expected.Columns.All(_ => _.Name != x.Name)).ToArray();
            Matched = expected.Columns.Intersect(actual.Columns).ToArray();
            Different =
                expected.Columns.Where(x => actual.HasColumn(x.Name) && !x.Equals(actual.Column(x.Name))).ToArray();

            _tableName = expected.Table;
            _expected = expected;
        }

        public TableColumn[] Different { get; set; }

        public TableColumn[] Matched { get; set; }

        public TableColumn[] Extras { get; set; }

        public TableColumn[] Missing { get; set; }

        public bool Matches => Missing.Count() + Extras.Count() + Different.Count() == 0;

        public bool CanPatch()
        {
            return !Different.Any();
        }

        public void CreatePatch(DocumentMapping mapping, Action<string> executeSql)
        {
            var systemFields = new string[] {DocumentMapping.LastModifiedColumn, DocumentMapping.DotNetTypeColumn, DocumentMapping.VersionColumn};

            var fields = Missing.Where(x => !systemFields.Contains(x.Name)).Select(x => mapping.FieldForColumn(x.Name)).ToArray();
            if (fields.Length != Missing.Length)
            {
                throw new InvalidOperationException("The expected columns did not match with the DocumentMapping");
            }


            var missingSystemColumns = Missing.Where(x => systemFields.Contains(x.Name)).ToArray();
            if (missingSystemColumns.Any())
            {
                missingSystemColumns.Each(col =>
                {
                    var patch =
                        $"alter table {_tableName.QualifiedName} add column {col.ToDeclaration(col.Name.Length + 1)}";

                    executeSql(patch);
                });
            }

            if (Missing.Select(x => x.Name).Contains(DocumentMapping.LastModifiedColumn))
            {
                executeSql($"alter table {_tableName.QualifiedName} add column ");
            }

            fields.Each(x => x.WritePatch(mapping, executeSql));
        }
    }
}