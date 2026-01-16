using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Grouping;
using Marten;
using Marten.Events.Aggregation;

namespace DaemonTests.TeleHealth;

#region sample_ProviderShift

public class ProviderShift(Guid boardId, Provider provider)
{
    public Guid Id { get; set; }
    public int Version { get; set; }
    public Guid BoardId { get; private set; } = boardId;
    public Guid ProviderId => Provider.Id;
    public ProviderStatus Status { get; set; } = ProviderStatus.Paused;
    public string Name { get; init; }
    public Guid? AppointmentId { get; set; }

    // I was admittedly lazy in the testing, so I just
    // completely embedded the Provider document directly
    // in the ProviderShift for easier querying later
    public Provider Provider { get; set; } = provider;
}

#endregion

public enum ProviderStatus
{
    Ready,
    Assigned,
    Charting,
    Paused
}

public record ProviderScheduled(Guid ProviderId, DateTimeOffset ExpectedStart);

public record AppointmentAssigned(Guid AppointmentId);

#region sample_ProviderJoined

public record ProviderJoined(Guid BoardId, Guid ProviderId);

#endregion

#region sample_EnhancedProviderJoined

public record EnhancedProviderJoined(Guid BoardId, Provider Provider);

#endregion
public record ProviderReady;
public record ProviderPaused;
public record ProviderSignedOff;

public record ChartingFinished;

public record ChartingStarted;

#region sample_ProviderShiftProjection

public class ProviderShiftProjection: SingleStreamProjection<ProviderShift, Guid>
{
    public ProviderShiftProjection()
    {
        // Make sure this is turned on!
        Options.CacheLimitPerTenant = 1000;
    }

    #region sample_ProviderShift_EnrichEventsAsync

    public override async Task EnrichEventsAsync(SliceGroup<ProviderShift, Guid> group, IQuerySession querySession, CancellationToken cancellation)
    {
        await group

            // First, let's declare what document type we're going to look up
            .EnrichWith<Provider>()

            // What event type or marker interface type or common abstract type
            // we could look for within each EventSlice that might reference
            // providers
            .ForEvent<ProviderJoined>()

            // Tell Marten how to find an identity to look up
            .ForEntityId(x => x.ProviderId)

            // And finally, execute the look up in one batched round trip,
            // and apply the matching data to each combination of EventSlice, event within that slice
            // that had a reference to a ProviderId, and the Provider
            .EnrichAsync((slice, e, provider) =>
            {
                // In this case we're swapping out the persisted event with the
                // enhanced event type before each event slice is then passed
                // in for updating the ProviderShift aggregates
                slice.ReplaceEvent(e, new EnhancedProviderJoined(e.Data.BoardId, provider));
            });
    }

    #endregion

    #region sample_ProviderShift_Evolve

    public override ProviderShift Evolve(ProviderShift snapshot, Guid id, IEvent e)
    {
        switch (e.Data)
        {
            case EnhancedProviderJoined joined:
                snapshot = new ProviderShift(joined.BoardId, joined.Provider)
                {
                    Provider = joined.Provider, Status = ProviderStatus.Ready
                };
                break;

            case ProviderReady:
                snapshot.Status = ProviderStatus.Ready;
                break;

            case AppointmentAssigned assigned:
                snapshot.Status = ProviderStatus.Assigned;
                snapshot.AppointmentId = assigned.AppointmentId;
                break;

            case ProviderPaused:
                snapshot.Status = ProviderStatus.Paused;
                snapshot.AppointmentId = null;
                break;

            case ChartingStarted charting:
                snapshot.Status = ProviderStatus.Charting;
                break;
        }

        return snapshot;
    }

    #endregion

}

#endregion

