#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events.Daemon;
using Marten.Internal.Sessions;
using Marten.Internal.Storage;
using Marten.Services;
using Npgsql;

namespace Marten.Events.Daemon.Internals;

/// <summary>
///     Lightweight session specifically used to capture operations for a specific tenant
///     in the asynchronous projections
/// </summary>
internal class ProjectionDocumentSession: DocumentSessionBase, ITransactionParticipantRegistrar
{
    public ShardExecutionMode Mode { get; }

    public ProjectionDocumentSession(DocumentStore store,
        ISessionWorkTracker workTracker,
        SessionOptions sessionOptions, ShardExecutionMode mode): base(store, sessionOptions, sessionOptions.BuildAutoClosingLifetime(store), workTracker)
    {
        Mode = mode;
    }

    public override NpgsqlConnection Connection
    {
        get
        {
            throw new NotSupportedException(
                "It is not supported to use \"sticky\" connections inside of projections or subscriptions");
        }
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

    public void AddTransactionParticipant(ITransactionParticipant participant)
    {
        if (_workTracker is ProjectionUpdateBatch batch)
        {
            batch.AddTransactionParticipant(participant);
        }
    }

    /// <summary>
    /// #4667 Phase 3 — close the user-code escape hatch. When user-supplied
    /// <c>operations.LoadAsync&lt;X&gt;(...)</c> runs inside an aggregation
    /// projection's EvolveAsync, route it through
    /// <see cref="IDocumentStorage{T, TId}.LoadProjectedAsync"/> so the
    /// daemon's shared 10-wide parallel <c>Block&lt;EventSliceExecution&gt;</c>
    /// workers never touch <c>_session.Versions</c> / <c>_session.ItemMap</c> /
    /// <c>_session.ChangeTrackers</c>. The opt-in
    /// <see cref="Marten.Events.EventGraph.UseIdentityMapForAggregates"/>
    /// path falls through to the base session-aware route (the GH-3850
    /// inline-projection identity-map semantics; documented as not safe for
    /// parallel projection workers).
    /// </summary>
    // Return is bare Task<T> (with [return: MaybeNull] on the base) — see the
    // chokepoint declaration in QuerySession.Load.cs for why.
    [return: MaybeNull]
    protected internal override Task<T> ExecuteLoadOneAsync<T, TId>(IDocumentStorage<T, TId> storage, TId id, CancellationToken token)
        => Options.EventGraph.UseIdentityMapForAggregates
            ? base.ExecuteLoadOneAsync(storage, id, token)
            : storage.LoadProjectedAsync(id, Database, TenantId, token)!;

    /// <inheritdoc cref="ExecuteLoadOneAsync{T, TId}"/>
    protected internal override Task<IReadOnlyList<T>> ExecuteLoadManyAsync<T, TId>(IDocumentStorage<T, TId> storage, TId[] ids, CancellationToken token)
        => Options.EventGraph.UseIdentityMapForAggregates
            ? base.ExecuteLoadManyAsync(storage, ids, token)
            : storage.LoadManyProjectedAsync(ids, Database, TenantId, token);
}
