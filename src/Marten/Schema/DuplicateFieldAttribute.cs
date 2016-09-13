using System;
using System.Reflection;
using Baseline;

namespace Marten.Schema
{
    /// <summary>
    /// Mark a single property or field on a document as a duplicated, searchable field
    /// for optimized searching
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class DuplicateFieldAttribute : MartenAttribute
    {
        public override void Modify(DocumentMapping mapping, MemberInfo member)
        {
            var field = mapping.DuplicateField(member.Name, PgType);
            var indexDefinition = mapping.AddIndex(field.ColumnName);
            indexDefinition.Method = IndexMethod;
            if (IndexName.IsNotEmpty())
                indexDefinition.IndexName = IndexName;
        }

        /// <summary>
        /// Use to override the Postgresql database column type of this searchable field
        /// </summary>
        public string PgType { get; set; } = null;

        /// <summary>
        /// Specifies the type of index to create
        /// </summary>
        public IndexMethod IndexMethod { get; set; } = IndexMethod.btree;

        /// <summary>
        /// Specify the name of the index explicity
        /// </summary>
        public string IndexName { get; set; } = null;
    }
}