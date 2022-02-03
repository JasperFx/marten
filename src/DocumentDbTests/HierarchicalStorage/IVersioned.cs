using System;

namespace DocumentDbTests.HierarchicalStorage
{
    public interface IVersioned
    {
        /// <summary>The unique ID of a version of the document</summary>
        Guid VersionId { get; set; }

        /// <summary>ID that remains the same for all versions of a document</summary>
        Guid DocumentId { get; set; }
    }
}