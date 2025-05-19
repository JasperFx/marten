using System;
using JasperFx.Events.Projections;

namespace Marten.Events.Projections;

public enum SnapshotLifecycle
{
    /// <summary>
    ///     The snapshot will be updated in the same transaction as
    ///     the events being captured
    /// </summary>
    Inline,

    /// <summary>
    ///     The snapshot will be made asynchronously within the Async Daemon
    /// </summary>
    Async
}

public static class SnapshotLifecycleExtensions
{
    public static SnapshotLifecycle Map(this ProjectionLifecycle projectionLifecycle) =>
        projectionLifecycle switch
        {
            ProjectionLifecycle.Inline => SnapshotLifecycle.Inline,
            ProjectionLifecycle.Async => SnapshotLifecycle.Async,
            ProjectionLifecycle.Live => throw new ArgumentOutOfRangeException(nameof(projectionLifecycle),
                "Snapshot lifecycle cannot be live!"),
            _ => throw new ArgumentOutOfRangeException(nameof(projectionLifecycle), projectionLifecycle, null)
        };

    public static ProjectionLifecycle Map(this SnapshotLifecycle projectionLifecycle) =>
        projectionLifecycle switch
        {
            SnapshotLifecycle.Inline => ProjectionLifecycle.Inline,
            SnapshotLifecycle.Async => ProjectionLifecycle.Async,
            _ => throw new ArgumentOutOfRangeException(nameof(projectionLifecycle), projectionLifecycle, null)
        };
}
