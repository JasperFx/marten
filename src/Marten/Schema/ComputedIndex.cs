using System.Reflection;
using Baseline;
using Marten.Util;

namespace Marten.Schema
{
    public class ComputedIndex : IIndexDefinition
    {
        private readonly MemberInfo[] _members;
        private readonly string _locator;
        private readonly DbObjectName _table;
        private string _indexName;

        public ComputedIndex(DocumentMapping mapping, MemberInfo[] members)
        {
            _members = members;
            var field = mapping.FieldFor(members);
            _locator = field.SqlLocator.Replace("d.", "");
            _table = mapping.Table;
        }

        /// <summary>
        /// Creates the index as UNIQUE
        /// </summary>
        public bool IsUnique { get; set; }

        /// <summary>
        /// Specifies the index should be created in the background and not block/lock
        /// </summary>
        public bool IsConcurrent { get; set; }

        /// <summary>
        /// Specify the name of the index explicity
        /// </summary>
        public string IndexName
        {
            get
            {
                if (_indexName.IsNotEmpty())
                {
                    return DocumentMapping.MartenPrefix + _indexName;
                }

                return GenerateIndexName();
            }
            set { _indexName = value; }
        }

        /// <summary>
        /// Allows you to specify a where clause on the index
        /// </summary>
        public string Where { get; set; }

        /// <summary>
        /// Marks the column value as upper/lower casing
        /// </summary>
        public Casings Casing { get; set; }

        /// <summary>
        /// Specifies the type of index to create
        /// </summary>
        public IndexMethod Method { get; set; } = IndexMethod.btree;

        public string ToDDL()
        {
            var index = IsUnique ? "CREATE UNIQUE INDEX" : "CREATE INDEX";

            if (IsConcurrent)
            {
                index += " CONCURRENTLY";
            }

            index += $" {IndexName} ON {_table.QualifiedName}";
            
            if (Method != IndexMethod.btree)
            {
                index += $" USING {Method}";
            }

            switch (Casing)
            {
                case Casings.Upper:
                    index += $" (upper({_locator}))";
                    break;
                case Casings.Lower:
                    index += $" (lower({_locator}))";
                    break;
                default:
                    index += $" (({_locator}))";
                    break;
            }

            if (Where.IsNotEmpty())
            {
                index += $" WHERE ({Where})";
            }

            return index + ";";
        }

        private string GenerateIndexName()
        {
            var name = _table.Name;

            name += IsUnique ? "_uidx_" : "_idx_";

            name += _members.ToTableAlias();

            return name;
        }

        public bool Matches(ActualIndex index)
        {
            return index != null;
        }

        public enum Casings
        {
            /// <summary>
            /// Leave the casing as is (default)
            /// </summary>
            Default,
            /// <summary>
            /// Change the casing to uppercase
            /// </summary>
            Upper,
            /// <summary>
            /// Change the casing to lowercase
            /// </summary>
            Lower
        }
    }
}