#nullable enable
namespace Marten.Metadata;

/// <summary>
///     Optionally implement this interface on your Marten document
///     types to opt into optimistic concurrency with the version
///     being tracked on the Version property using numeric revision values
/// </summary>
public interface IRevisioned
{
    /// <summary>
    ///     Marten's version for this document
    /// </summary>
    int Version { get; set; }
}
