using System;

namespace Marten.Metadata
{
    /// <summary>
    /// Optionally implement this interface on your Marten document
    /// types to opt into "soft delete" mechanics with the deletion
    /// information tracked directly on the documents
    /// </summary>
    public interface ISoftDeleted
    {
        /// <summary>
        /// Has Marten marked this document as soft deleted
        /// </summary>
        bool Deleted {get;set;}

        /// <summary>
        /// When was this document marked as deleted by Marten
        /// </summary>
        DateTimeOffset? DeletedAt {get;set;}
    }
}
