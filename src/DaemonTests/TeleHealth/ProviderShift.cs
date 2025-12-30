using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Grouping;
using Marten;
using Marten.Events.Aggregation;

namespace DaemonTests.TeleHealth;

public class ProviderShift(Guid boardId, Provider provider)
{
    public Guid Id { get; set; }
    public int Version { get; set; }
    public Guid BoardId { get; private set; } = boardId;
    public Guid ProviderId => Provider.Id;
    public ProviderStatus Status { get; set; }
    public string Name { get; init; }
    public Guid? AppointmentId { get; set; }
    public Provider Provider { get; set; } = provider;
}

public enum ProviderStatus
{
    Ready,
    Assigned,
    Charting,
    Paused
}

public record ProviderScheduled(Guid ProviderId, DateTimeOffset ExpectedStart);

public record AppointmentAssigned(Guid AppointmentId);
public record ProviderJoined(Guid BoardId, Guid ProviderId);
public record EnhancedProviderJoined(Guid BoardId, Provider Provider);
public record ProviderReady;
public record ProviderPaused;
public record ProviderSignedOff;

public record ChartingFinished;

public record ChartingStarted;

public class ProviderShiftProjection: SingleStreamProjection<ProviderShift, Guid>
{
    public ProviderShiftProjection()
    {
        // Make sure this is turned on!
        Options.CacheLimitPerTenant = 1000;
    }

    public override async Task EnrichEventsAsync(SliceGroup<ProviderShift, Guid> group, IQuerySession querySession, CancellationToken cancellation)
    {
        await group
            .EnrichWith<Provider>()
            .ForEvent<ProviderJoined>()
            .ForEntityId(x => x.ProviderId)
            .EnrichAsync((slice, e, provider) =>
            {
                slice.ReplaceEvent(e, new EnhancedProviderJoined(e.Data.BoardId, provider));
            });
    }

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

}

