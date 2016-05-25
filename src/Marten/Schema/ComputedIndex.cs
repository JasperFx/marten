using Baseline;
using Marten.Util;

namespace Marten.Schema
{
    public class ComputedIndex : IIndexDefinition
    {
        private readonly TableName _table;
        private readonly string _locator;

        public ComputedIndex(TableName table, string memberName, string locator)
        {
            _table = table;
            _locator = locator;

            IndexName = $"{table.Name}_idx_{memberName.ToTableAlias()}";
        }

        public string IndexName { get; }

        public string ToDDL()
        {
            return $"CREATE INDEX {IndexName} ON {_table.QualifiedName} (({_locator}))";
        }

        public bool Matches(ActualIndex index)
        {
            return index.DDL.EqualsIgnoreCase(ToDDL());
        }
    }
}