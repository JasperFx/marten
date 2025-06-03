using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events.Daemon;
using Marten.Internal.Sessions;
using Marten.Internal.Storage;
using Marten.Services;

namespace Marten.Events.Daemon.Internals;

/// <summary>
///     Lightweight session specifically used to capture operations for a specific tenant
///     in the asynchronous projections
/// </summary>
internal class ProjectionDocumentSession: DocumentSessionBase
{
    public ShardExecutionMode Mode { get; }

    public ProjectionDocumentSession(DocumentStore store,
        ISessionWorkTracker workTracker,
        SessionOptions sessionOptions, ShardExecutionMode mode): base(store, sessionOptions, new AutoClosingLifetime(sessionOptions, store.Options), workTracker)
    {
        Mode = mode;
    }

    internal override DocumentTracking TrackingMode => SessionOptions.Tracking;

    protected internal override IDocumentStorage<T> selectStorage<T>(DocumentProvider<T> provider) =>
        TrackingMode == DocumentTracking.IdentityOnly ? provider.IdentityMap : provider.Lightweight;

    // Do nothing here! See GH-3167
    protected override Task tryApplyTombstoneEventsAsync(CancellationToken token)
    {
        return Task.CompletedTask;
    }

    protected internal override void ejectById<T>(long id)
    {
        // nothing
    }

    protected internal override void ejectById<T>(int id)
    {
        // nothing
    }

    protected internal override void ejectById<T>(Guid id)
    {
        // nothing
    }

    protected internal override void ejectById<T>(string id)
    {
        // nothing
    }
}
