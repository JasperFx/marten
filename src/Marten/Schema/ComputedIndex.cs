using System.Linq;
using System.Reflection;
using Baseline;
using Marten.Schema.Indexing.Unique;
using Marten.Storage;
using Marten.Util;

namespace Marten.Schema
{
    public class ComputedIndex: IIndexDefinition
    {
        private readonly MemberInfo[][] _members;
        private readonly DocumentMapping _mapping;
        private string _indexName;

        public ComputedIndex(DocumentMapping mapping, MemberInfo[] memberPath)
            : this(mapping, new[] { memberPath })
        {
        }

        public ComputedIndex(DocumentMapping mapping, MemberInfo[][] members)
        {
            _members = members;
            _mapping = mapping;
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
                    return DocumentMapping.MartenPrefix + _indexName.ToLowerInvariant();
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

        /// <summary>
        /// Specifies the sort order of the index (only applicable to B-tree indexes)
        /// </summary>
        public SortOrder SortOrder { get; set; } = SortOrder.Asc;

        /// <summary>
        /// Specifies the unique index is scoped to the tenant
        /// </summary>
        public TenancyScope TenancyScope { get; set; }

        public string ToDDL()
        {
            var index = IsUnique ? "CREATE UNIQUE INDEX" : "CREATE INDEX";

            if (IsConcurrent)
            {
                index += " CONCURRENTLY";
            }

            index += $" {IndexName} ON {_mapping.Table.QualifiedName}";

            if (Method != IndexMethod.btree)
            {
                index += $" USING {Method}";
            }

            var membersLocator = _members
                .Select(m =>
                {
                    var sql = _mapping.FieldFor(m).SqlLocator.Replace("d.", "");
                    switch (Casing)
                    {
                        case Casings.Upper:
                            return $" upper({sql})";

                        case Casings.Lower:
                            return $" lower({sql})";

                        default:
                            return $" ({sql})";
                    }
                })
                .Join(",");

            var locator = $"{membersLocator}";

            if (TenancyScope == TenancyScope.PerTenant)
            {
                locator = $"{locator}, tenant_id";
            }

            index += " (" + locator + ")";

            // Only the B-tree index type supports modifying the sort order, and ascending is the default
            if (Method == IndexMethod.btree && SortOrder == SortOrder.Desc)
            {
                index = index.Remove(index.Length - 1);
                index += " DESC)";
            }

            if (Where.IsNotEmpty())
            {
                index += $" WHERE ({Where})";
            }

            return index + ";";
        }

        private string GenerateIndexName()
        {
            var name = _mapping.Table.Name;

            name += IsUnique ? "_uidx_" : "_idx_";

            foreach (var member in _members)
            {
                name += member.ToTableAlias();
            }

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
