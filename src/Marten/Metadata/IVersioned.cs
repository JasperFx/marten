using System;
#nullable enable
namespace Marten.Metadata
{
    /// <summary>
    /// Optionally implement this interface on your Marten document
    /// types to opt into optimistic concurrency with the version
    /// being tracked on the Version property
    /// </summary>
    public interface IVersioned
    {
        /// <summary>
        /// Marten's version for this document
        /// </summary>
        Guid Version { get; set; }
    }
}
