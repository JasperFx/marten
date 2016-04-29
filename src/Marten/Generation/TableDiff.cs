using System;
using System.Linq;
using Baseline;
using Marten.Schema;

namespace Marten.Generation
{
    public class TableDiff
    {
        public TableDiff(TableDefinition expected, TableDefinition actual)
        {
            Missing = expected.Columns.Where(x => actual.Columns.All(_ => _.Name != x.Name)).ToArray();
            Extras = actual.Columns.Where(x => expected.Columns.All(_ => _.Name != x.Name)).ToArray();
            Matched = expected.Columns.Intersect(actual.Columns).ToArray();
            Different =
                expected.Columns.Where(x => actual.HasColumn(x.Name) && !x.Equals(actual.Column(x.Name))).ToArray();
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
            var fields = Missing.Select(x => mapping.FieldForColumn(x.Name)).ToArray();
            if (fields.Length != Missing.Length)
            {
                throw new InvalidOperationException("The expected columns did not match with the DocumentMapping");
            }

            fields.Each(x => x.WritePatch(mapping, executeSql));
        }
    }
}