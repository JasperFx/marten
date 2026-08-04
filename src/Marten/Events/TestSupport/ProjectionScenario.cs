using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.TestSupport;

namespace Marten.Events.TestSupport;

/// <summary>
///     Marten's implementation of the JasperFx.Events projection scenario test harness,
///     closing the generic session pair over <see cref="IDocumentOperations" /> /
///     <see cref="IQuerySession" />. All scripting and execution behavior lives on the
///     <see cref="ProjectionScenario{TOperations,TQuerySession}" /> base type; this class
///     only supplies the store-specific seam
/// </summary>
public class ProjectionScenario: JasperFx.Events.TestSupport.ProjectionScenario<IDocumentOperations, IQuerySession>
{
    private readonly DocumentStore _store;

    internal ProjectionScenario(DocumentStore store)
    {
        _store = store;
    }

    /// <summary>
    ///     #5169: the wipe list comes from <see cref="IProjectionSource{TOperations,TQuerySession}.PublishedTypes" />,
    ///     not <c>Options.StorageTypes</c>. A <c>CompositeProjection</c> never populates its own
    ///     <c>StorageTypes</c> — its members hold theirs — so the loop iterated nothing for a store whose read
    ///     side is a composite, and every scenario after the first ran against the previous scenario's
    ///     documents while their events had already been deleted. <c>PublishedTypes()</c> is the traversal that
    ///     already knows to expand a composite into its members (and is a superset of <c>StorageTypes</c> for
    ///     everything else), which is why Marten's own schema build-out uses it.
    /// </summary>
    protected override async Task DeleteExistingDataAsync(CancellationToken ct)
    {
        await _store.Advanced.Clean.DeleteAllEventDataAsync(ct).ConfigureAwait(false);
        foreach (var storageType in
                 _store.Options.Projections.All.SelectMany(x => x.PublishedTypes()).Distinct())
        {
            await _store.Advanced.Clean.DeleteDocumentsByTypeAsync(storageType, ct).ConfigureAwait(false);
        }
    }

    protected override bool HasAnyAsyncProjections => _store.Options.Projections.HasAnyAsyncProjections();

    protected override async ValueTask<IProjectionDaemon> BuildDaemonAsync(string? tenantId)
    {
        return await _store.BuildProjectionDaemonAsync(tenantId).ConfigureAwait(false);
    }

    protected override IDocumentOperations OpenSession(string? tenantId)
    {
        return tenantId.IsNotEmpty() ? _store.LightweightSession(tenantId) : _store.LightweightSession();
    }

    // No shared JasperFx interface declares SaveChangesAsync -- in Marten it lives on
    // IDocumentSession, which every session handed out by OpenSession() actually is.
    protected override Task SaveChangesAsync(IDocumentOperations session, CancellationToken ct)
    {
        return ((IDocumentSession)session).SaveChangesAsync(ct);
    }

    protected override IEventOperations EventsFor(IDocumentOperations session)
    {
        return session.Events;
    }

    protected override Task<T?> LoadDocumentAsync<T>(IQuerySession session, object id, CancellationToken ct)
        where T : class
    {
        return id switch
        {
            Guid guidId => session.LoadAsync<T>(guidId, ct),
            int intId => session.LoadAsync<T>(intId, ct),
            long longId => session.LoadAsync<T>(longId, ct),
            string stringId => session.LoadAsync<T>(stringId, ct),
            _ => throw new ArgumentOutOfRangeException(nameof(id),
                $"Marten cannot load documents by an identity of type {id.GetType().FullName}")
        };
    }
}
