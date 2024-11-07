using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events.Projections;
using Marten.Events.Daemon.Internals;
using Marten.Internal.Sessions;
using Marten.Services;
using Marten.Storage;

namespace Marten.Subscriptions;

internal class SubscriptionRunner : ISubscriptionRunner
{
    private readonly ISubscription _subscription;
    private readonly DocumentStore _store;
    private readonly IMartenDatabase _database;

    public SubscriptionRunner(ShardName shard, ISubscription subscription, DocumentStore store, IMartenDatabase database)
    {
        _subscription = subscription;
        _store = store;
        _database = database;
        ShardIdentity = shard.Identity;
        if (database.Identifier != "Marten")
        {
            ShardIdentity += $"@{database.Identifier}";
        }
    }

    public string DatabaseIdentifier => _database.Identifier;

    public string ShardIdentity { get; }
    public async Task ExecuteAsync(EventRange range, ShardExecutionMode mode, CancellationToken cancellation)
    {
        // START HERE -->
        // Encapsulate this
        await using var parent = (DocumentSessionBase)_store.OpenSession(SessionOptions.ForDatabase(_database));

        var batch = new ProjectionUpdateBatch(_store.Options.Projections, parent, mode, cancellation)            {
            ShouldApplyListeners = mode == ShardExecutionMode.Continuous && range.Events.Any()
        };;

        // Mark the progression
        batch.Queue.Post(range.BuildProgressionOperation(_store.Events));

        await using var session = new ProjectionDocumentSession(_store, batch,
            new SessionOptions
            {
                Tracking = DocumentTracking.IdentityOnly,
                Tenant = new Tenant(StorageConstants.DefaultTenantId, _database)
            }, mode);


        var listener = await _subscription.ProcessEventsAsync(range, range.Agent, session, cancellation)
            .ConfigureAwait(false);

        batch.Listeners.Add(listener);
        await batch.WaitForCompletion().ConfigureAwait(false);

        // Polly is already around the basic retry here, so anything that gets past this
        // probably deserves a full circuit break
        await session.ExecuteBatchAsync(batch, cancellation).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_subscription != null)
        {
            await _subscription.DisposeAsync().ConfigureAwait(false);
        }
    }
}
