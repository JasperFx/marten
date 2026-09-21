using System;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten.Events;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.EntityFrameworkCore.Tests;

public record TrackedOrderPlaced(Guid OrderId, string CustomerName, decimal Amount);

public class DisposalProbeProjection
    : EfCoreSingleStreamProjection<TrackedOrder, Guid, DisposalTrackingDbContext>
{
    public override TrackedOrder ApplyEvent(TrackedOrder? snapshot, Guid identity, IEvent @event,
        DisposalTrackingDbContext dbContext, IQuerySession session)
    {
        snapshot ??= new TrackedOrder { Id = identity };

        if (@event.Data is TrackedOrderPlaced placed)
        {
            snapshot.CustomerName = placed.CustomerName;
            snapshot.TotalAmount = placed.Amount;
        }

        return snapshot;
    }
}

/// <summary>
/// #5457. <c>EfCoreProjectionStorage</c> owns a <see cref="Microsoft.EntityFrameworkCore.DbContext"/>
/// created once per tenant per batch, and nothing disposed it -- not the storage (which implements no
/// disposal contract at all), not the daemon, not the session. Only the separately registered
/// <c>DbContextTransactionParticipant</c> was disposed, and it released only the placeholder
/// connection Marten handed EF Core, never the context itself.
///
/// <para>
/// The reported symptom was a backend leak, which needs the context to own its connection to show
/// up. These tests pin the defect one level below that symptom: a context is created per batch and
/// must be disposed per batch. That holds regardless of who owns the connection, and it is
/// deterministic -- no pool timing, no <c>pg_stat_activity</c> sampling, no dependence on the
/// PostgreSQL or Npgsql version the reporter happened to be on.
/// </para>
/// </summary>
[Collection("OneOffs")]
public class Bug_5457_efcore_dbcontext_never_disposed: IAsyncLifetime
{
    private const string SchemaName = "efcore_disposal_5457";
    private const int Batches = 5;

    private DocumentStore theStore = null!;

    public async ValueTask InitializeAsync()
    {
        theStore = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = SchemaName;
            opts.Add(new DisposalProbeProjection(), ProjectionLifecycle.Async);
        });

        await theStore.Advanced.Clean.CompletelyRemoveAllAsync();

        // Force the schema into existence before the counters start, so the contexts counted below
        // are only the ones the batches create.
        await AppendOneStreamAsync();
    }

    public ValueTask DisposeAsync()
    {
        theStore?.Dispose();
        return default;
    }

    [Fact]
    public async Task every_dbcontext_the_daemon_creates_is_disposed()
    {
        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();
        await theStore.WaitForNonStaleProjectionDataAsync(30.Seconds());

        DisposalTrackingDbContext.ResetCounts();

        for (var i = 0; i < Batches; i++)
        {
            await AppendOneStreamAsync();
            await theStore.WaitForNonStaleProjectionDataAsync(30.Seconds());
        }

        var created = DisposalTrackingDbContext.Created;
        var disposed = DisposalTrackingDbContext.Disposed;

        // Guard against a vacuously green test: if the daemon never built a context, there was
        // nothing that could have leaked and the assertion below would pass for the wrong reason.
        created.ShouldBeGreaterThanOrEqualTo(Batches,
            "the daemon did not build a DbContext per batch, so this test proves nothing");

        disposed.ShouldBe(created,
            $"{created} DbContext instances were created across {Batches} daemon batches but only "
            + $"{disposed} were disposed");
    }

    private async Task AppendOneStreamAsync()
    {
        await using var session = theStore.LightweightSession();
        session.Events.StartStream(Guid.NewGuid(),
            new TrackedOrderPlaced(Guid.NewGuid(), "customer", 10m));
        await session.SaveChangesAsync(TestContext.Current.CancellationToken);
    }
}
