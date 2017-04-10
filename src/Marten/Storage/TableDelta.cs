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

        public override string ToString()
        {
            return $"TableDiff for {_tableName}";
        }
    }
}