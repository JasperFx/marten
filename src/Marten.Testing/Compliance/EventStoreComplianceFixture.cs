using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events.Daemon;
using Xunit;

namespace JasperFx.Events.ComplianceTests;

/// <summary>
/// The seam between the shared event sourcing compliance suites and a concrete event store.
/// Every portable operation in the suites flows through the shared JasperFx surfaces
/// (<see cref="IEventStoreOperations"/>, <see cref="IEventRegistry"/>, <see cref="IProjectionDaemon"/>);
/// this type only exists to absorb the handful of things no shared interface declares —
/// store construction, session acquisition, SaveChanges, document load-back, batched DCB queries,
/// and teardown.
/// </summary>
/// <typeparam name="TOperations">
/// The store's writable session type — Marten <c>IDocumentOperations</c>, Polecat <c>IDocumentSession</c>.
/// </typeparam>
/// <typeparam name="TQuerySession">The store's read-only session type.</typeparam>
/// <remarks>
/// The generic closure mirrors JasperFx's own <c>IEventStore&lt;TOperations, TQuerySession&gt;</c>.
/// The products deliberately close it differently and convergence is a non-goal, so the compliance
/// library is generic over the same pair rather than trying to unify them.
/// </remarks>
public abstract class EventStoreComplianceFixture<TOperations, TQuerySession> : IAsyncLifetime
    where TOperations : TQuerySession, IStorageOperations
{
    private Action<ComplianceStoreConfig>? _lastConfiguration;

    /// <summary>
    /// Cancellation token handed to every store call the suites make. Overridable rather than
    /// hard-coded so a consumer can swap in its own budget.
    /// </summary>
    public virtual CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>
    /// Build (or rebuild) the store for the supplied configuration.
    /// </summary>
    /// <remarks>
    /// Deliberately keyed on the identity of the <paramref name="configure"/> delegate: suites hold
    /// their standard configuration in a static field, so repeated calls across the test methods of
    /// one class are free, while a test that deliberately passes a different delegate gets a real
    /// rebuild. Async because some stores (Polecat) must apply schema changes explicitly.
    /// </remarks>
    public async Task ConfigureAsync(Action<ComplianceStoreConfig> configure)
    {
        if (ReferenceEquals(_lastConfiguration, configure))
        {
            return;
        }

        var config = new ComplianceStoreConfig();
        configure(config);

        await BuildStoreAsync(config).ConfigureAwait(false);

        _lastConfiguration = configure;
    }

    /// <summary>
    /// Construct the store from the store-neutral configuration, replaying it through an
    /// <see cref="IComplianceStoreRegistrar"/>, and make sure its schema exists.
    /// </summary>
    protected abstract Task BuildStoreAsync(ComplianceStoreConfig config);

    /// <summary>
    /// Open a writable session. Callers dispose it — <see cref="IStorageOperations"/> is
    /// <see cref="IAsyncDisposable"/> on both stores.
    /// </summary>
    public abstract TOperations OpenSession();

    public abstract Task SaveChangesAsync(TOperations session, CancellationToken token);

    /// <summary>
    /// Load a persisted document by id. Distinct from re-folding a stream: this is what proves an
    /// inline snapshot projection actually wrote something.
    /// </summary>
    public abstract Task<T?> LoadDocumentAsync<T>(TQuerySession session, object id, CancellationToken token)
        where T : class;

    /// <summary>
    /// The payoff member — everything portable in the suites runs off the shared JasperFx surface.
    /// </summary>
    public abstract IEventStoreOperations EventsFor(TOperations session);

    public abstract IComplianceBatch CreateBatch(TQuerySession session);

    /// <summary>
    /// The store's event registry, reached through the shared interface so assertions can use
    /// <c>EventMappingFor(Type)</c> → <see cref="IEventType"/> with no InternalsVisibleTo.
    /// </summary>
    public abstract IEventRegistry Registry { get; }

    /// <summary>
    /// Per-test isolation: remove all event (and projected document) data without dropping schema.
    /// </summary>
    public abstract Task CleanEventDataAsync();

    public abstract Task<IProjectionDaemon> StartDaemonAsync();

    public abstract Task WaitForNonStaleProjectionDataAsync(TimeSpan timeout);

    /// <summary>
    /// False in stores that build live aggregators automatically and reject explicit registration.
    /// </summary>
    public virtual bool SupportsLiveAggregationRegistration => true;

    /// <summary>
    /// False where the store cannot run the async projection daemon in the test environment.
    /// </summary>
    public virtual bool SupportsAsyncDaemon => true;

    public virtual ValueTask InitializeAsync() => default;

    public virtual ValueTask DisposeAsync() => default;
}
