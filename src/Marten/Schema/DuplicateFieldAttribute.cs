using System;
using System.Reflection;
using Baseline;
using NpgsqlTypes;
#nullable enable
namespace Marten.Schema
{
    /// <summary>
    /// Mark a single property or field on a document as a duplicated, searchable field
    /// for optimized searching
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class DuplicateFieldAttribute: MartenAttribute
    {
        public override void Modify(DocumentMapping mapping, MemberInfo member)
        {
            var field = mapping.DuplicateField(member.Name, PgType, notNull: NotNull);

            if (DbType != default)
            {
                field.DbType = DbType;
            }

            var indexDefinition = mapping.AddIndex(field.ColumnName);
            indexDefinition.Method = IndexMethod;
            if (IndexName.IsNotEmpty())
                indexDefinition.IndexName = IndexName;

            indexDefinition.SortOrder = IndexSortOrder;
        }

        /// <summary>
        /// Use to override the Postgresql database column type of this searchable field
        /// </summary>
        public string? PgType { get; set; } = null;

        /// <summary>
        /// Use to override the NpgsqlDbType used when querying with a parameter
        /// against the property
        /// </summary>
        public NpgsqlDbType DbType { get; set; }

        /// <summary>
        /// Specifies the type of index to create
        /// </summary>
        public IndexMethod IndexMethod { get; set; } = IndexMethod.btree;

        /// <summary>
        /// Specify the name of the index explicity
        /// </summary>
        public string? IndexName { get; set; } = null;

        /// <summary>
        /// Specifies the sort order of the index (only applicable to B-tree indexes)
        /// </summary>
        public SortOrder IndexSortOrder { get; set; } = SortOrder.Asc;

        public bool NotNull { get; set; } = false;
    }
}
