#nullable enable
using System;

namespace Marten.Schema;

/// <summary>
///     Marks a document type as "soft deleted"
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class SoftDeletedAttribute: MartenDocumentAttribute
{
    /// <summary>
    ///     Creates an index on deleted documents
    /// </summary>
    public bool Indexed { get; set; }

    /// <summary>
    /// Opt into using PostgreSQL table partitioning on the is deleted status
    /// as a performance optimization
    /// </summary>
    public bool UsePartitioning { get; set; }

    public override void Modify(DocumentMapping mapping)
    {
        mapping.DeleteStyle = DeleteStyle.SoftDelete;
        if (Indexed)
        {
            mapping.AddDeletedAtIndex();
        }

        if (UsePartitioning)
        {
            mapping.PartitionByDeleted();
        }
    }
}
