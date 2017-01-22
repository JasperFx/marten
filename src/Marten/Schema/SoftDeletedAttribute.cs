using System;

namespace Marten.Schema
{
    /// <summary>
    /// Marks a document type as "soft deleted"
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class SoftDeletedAttribute : MartenAttribute
    {
        public override void Modify(DocumentMapping mapping)
        {
            mapping.DeleteStyle = DeleteStyle.SoftDelete;
            if (!Indexed) return;
            mapping.AddDeletedAtIndex();
        }

        /// <summary>
        /// Creates an index on deleted documents
        /// </summary>
        public bool Indexed { get; set; }
    }
}
