#nullable enable
using System;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;

namespace Marten.Events.Daemon.HighWater;

/// <summary>
///     Patches <see cref="ShardState.SkippedEventsCount"/> on the HighWaterMark
///     state when the JasperFx.Events HighWaterAgent publishes a
///     <see cref="ShardAction.Skipped"/> event. The shared
///     <see cref="ShardStateTracker.MarkSkippingAsync"/> helper supplies
///     <see cref="ShardState.Sequence"/> and <see cref="ShardState.PreviousGoodMark"/>
///     but doesn't populate the count, so we fill it here on Marten's side using
///     the most-recent semantic (gap size = Sequence - PreviousGoodMark).
/// </summary>
internal sealed class SkippedEventsCountObserver: IObserver<ShardState>
{
    public void OnCompleted() { }

    public void OnError(Exception error) { }

    public void OnNext(ShardState value)
    {
        if (value.Action != ShardAction.Skipped) return;
        if (value.ShardName != ShardState.HighWaterMark) return;
        if (value.SkippedEventsCount.HasValue) return;

        var skipped = value.Sequence - value.PreviousGoodMark;
        if (skipped > 0)
        {
            value.SkippedEventsCount = skipped;
        }
    }
}
