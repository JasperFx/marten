using System.Reflection;
using Baseline;
using Marten.Util;

namespace Marten.Schema
{
    public class ComputedIndex : IIndexDefinition
    {
        private readonly string _locator;
        private readonly TableName _table;


        public ComputedIndex(DocumentMapping mapping, MemberInfo[] members)
        {
            var field = mapping.FieldFor(members);
            _locator = field.SqlLocator.Replace("d.", "");
            _table = mapping.Table;

            IndexName = $"{mapping.Table.Name}_idx_{members.ToTableAlias()}";
        }

        public string IndexName { get; }

        public string ToDDL()
        {
            return $"CREATE INDEX {IndexName} ON {_table.QualifiedName} (({_locator}))";
        }

        public bool Matches(ActualIndex index)
        {
            return index != null;
        }
    }
}