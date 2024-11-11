using System;
using JasperFx.Events.Projections;

namespace Marten.Events.Projections;

/// <summary>
///     Read-only diagnostic view of a registered projection
/// </summary>
public interface IReadOnlyProjectionData
{
    /// <summary>
    ///     The configured projection name used within the Async Daemon
    ///     progress tracking
    /// </summary>
    string ProjectionName { get; }

    /// <summary>
    ///     When is this projection executed?
    /// </summary>
    ProjectionLifecycle Lifecycle { get; }

    /// <summary>
    ///     The concrete .Net type implementing this projection
    /// </summary>
    Type ProjectionType { get; }
}
