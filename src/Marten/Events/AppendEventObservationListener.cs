#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten.Services;
using Npgsql;

namespace Marten.Events;

/// <summary>
/// Auto-registered on a <see cref="DocumentStore"/> when <c>JasperFxOptions.EnableAdvancedTracking</c>
/// is true (see <see cref="StoreOptions.ReadJasperFxOptions"/>). After each successful commit it
/// forwards the events appended in that unit of work to the storage-agnostic
/// <see cref="JasperFx.Events.IEventStoreInstrumentation.AppendObserver"/>, so lifecycle tooling such
/// as CritterWatch can record runtime-observed "appends" edges (#4782). Best-effort: a null observer or
/// a throwing observer never disrupts the unit of work.
/// </summary>
internal class AppendEventObservationListener: DocumentSessionListenerBase
{
    private readonly EventGraph _events;

    public AppendEventObservationListener(EventGraph events)
    {
        _events = events;
    }

    public override Task AfterCommitAsync(IDocumentSession session, IChangeSet commit, CancellationToken token)
    {
        var observer = _events.AppendObserver;
        if (observer is null)
        {
            return Task.CompletedTask;
        }

        var events = commit.GetEvents().ToList();
        if (events.Count == 0)
        {
            return Task.CompletedTask;
        }

        try
        {
            observer(events);
        }
        catch (Exception e)
        {
            session.Logger.LogFailure(new NpgsqlCommand(), e);
        }

        return Task.CompletedTask;
    }
}
