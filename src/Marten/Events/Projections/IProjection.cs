#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Events.Projections;

#region sample_IProjection

/// <summary>
///     Interface for all event projections
/// </summary>
public interface IProjection
{
    /// <summary>
    ///     Apply inline projections during synchronous operations
    /// </summary>
    /// <param name="operations"></param>
    /// <param name="streams"></param>
    void Apply(IDocumentOperations operations, IReadOnlyList<StreamAction> streams);

    /// <summary>
    ///     Apply inline projections during asynchronous operations
    /// </summary>
    /// <param name="operations"></param>
    /// <param name="streams"></param>
    /// <param name="cancellation"></param>
    /// <returns></returns>
    Task ApplyAsync(IDocumentOperations operations, IReadOnlyList<StreamAction> streams,
        CancellationToken cancellation);

    /// <summary>
    /// Enable the identity map mechanics to reuse documents within the session by their identity
    /// if a projection needs to make subsequent changes to the same document at one time. Default is no tracking
    /// </summary>
    bool EnableDocumentTrackingDuringRebuilds { get; set; }
}

#endregion
