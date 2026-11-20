#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten.Events.Daemon.Internals;
using Marten.Storage;
using Microsoft.Extensions.Logging;

namespace Marten;

// marten#6003: Marten's side of parking the failing slice (JasperFx 3.10): the parked table and the batch operations
// are in Marten.Events.Daemon.Parking, the reads in MartenDatabase.ParkedIdentities, and the identity sources here
public partial class DocumentStore
{
    private int _loggedProjectionParking;

    bool IEventStore<IDocumentOperations, IQuerySession>.SupportsParkedIdentities => true;

    // Stream, tag (tag tables or the hstore column), slice index and scan
    bool IEventStore<IDocumentOperations, IQuerySession>.SupportsIdentityEventSource(IdentityEventSource source)
        => true;

    Task<IReadOnlyList<IEvent>> IEventStore<IDocumentOperations, IQuerySession>.LoadEventsForIdentityAsync(
        IEventDatabase database, IdentityEventLoadRequest request, CancellationToken token)
        => new IdentityRebuildEventLoader(this, (MartenDatabase)database).LoadAsync(request, token);

    /// <summary>
    ///     Says once per store, when its first daemon is built, how each async projection finds one identity's events
    ///     and what a failing identity does: parks, pauses the shard, or is skipped.
    /// </summary>
    internal void LogProjectionParkingOnce(ILogger logger)
    {
        if (Interlocked.Exchange(ref _loggedProjectionParking, 1) == 1) return;
        if (!logger.IsEnabled(LogLevel.Information)) return;

        var errors = Options.Projections.Errors;
        foreach (var source in Options.Projections.All.Where(x => x.Lifecycle == ProjectionLifecycle.Async))
        {
            var access = (source as IHasIdentityAccess)?.IdentityAccess
                         ?? IdentityAccess.NotSupported("Only an aggregation projection has identities to park.");

            var action = errors.ApplyErrors switch
            {
                ProjectionErrorAction.Skip => "is skipped and dead-lettered",
                ProjectionErrorAction.Park when access.CanPark => "parks",
                _ => "pauses the shard"
            };

            logger.LogInformation(
                "Projection {Projection} finds one identity's events as {IdentityAccess}: a failing identity {Action}. {Reason}",
                source.Name, access.Class, action, access.Reason);
        }
    }
}
