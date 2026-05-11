using System;
using JasperFx.Events.Projections;

namespace Marten.Events.Projections;

// SnapshotLifecycle consolidated to JasperFx.Events per the dedup audit
// (jasperfx#220 / pillar #214). Marten previously declared its own
// byte-identical copy; it now aliases via GlobalUsings to keep call
// sites unchanged. The Map() extension methods below stay product-local
// since they're projection-registration concerns, not shared event-
// sourcing concepts.

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
