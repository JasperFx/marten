using System;
using System.Reflection;

namespace Marten.Schema
{
    /// <summary>
    /// Mark a single property or field on a document as a duplicated, searchable field
    /// for optimized searching
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class SearchableAttribute : MartenAttribute
    {
        public override void Modify(DocumentMapping mapping, MemberInfo member)
        {
            mapping.DuplicateField(member.Name, PgType);
        }

        /// <summary>
        /// Use to override the Postgresql database column type of this searchable field
        /// </summary>
        public string PgType { get; set; } = null;
    }
}