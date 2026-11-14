#nullable enable
using System;
using System.Threading.Tasks;
using Bobcat;
using Bobcat.CritterStack;
using JasperFx.Core;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace DaemonTests.Unions;

// #5735 / jasperfx#1053: C# 15 union events through Marten and the async daemon. net11.0 only
// (see DaemonTests.csproj).

public record LedgerOpened(string Owner);

public record LedgerCredited(decimal Amount);

public record LedgerDebited(decimal Amount, string Reason);

public union LedgerEntry(LedgerCredited, LedgerDebited);

/// <summary>An async snapshot with one Apply for the whole union.</summary>
public partial class UnionLedger
{
    public Guid Id { get; set; }
    public string Owner { get; set; } = string.Empty;
    public decimal Balance { get; set; }
    public int Debits { get; set; }

    public static UnionLedger Create(LedgerOpened e) => new() { Owner = e.Owner };

    public void Apply(LedgerEntry e)
    {
        switch (e)
        {
            case LedgerCredited credited:
                Balance += credited.Amount;
                break;
            case LedgerDebited debited:
                Balance -= debited.Amount;
                Debits++;
                break;
        }
    }
}

/// <summary>
/// An application with one async snapshot, <see cref="UnionLedger" />, built from union events, and the daemon
/// running in the application itself.
/// </summary>
public sealed class UnionLedgerHost: IAsyncLifetime
{
    public IHost Host { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        Host = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddMarten(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DatabaseSchemaName = "union_specs";
                    opts.UseSystemTextJsonForSerialization();
                    opts.Projections.Snapshot<UnionLedger>(SnapshotLifecycle.Async);
                }).AddAsyncDaemon(DaemonMode.Solo);
            })
            .Build();

        // Clean before the daemon starts; see ShipmentsHostBase for why.
        var store = Host.Services.GetRequiredService<IDocumentStore>();
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        await store.Advanced.Clean.DeleteAllEventDataAsync(TestContext.Current.CancellationToken);

        await Host.StartAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (Host is null) return;

        await Host.StopAsync();
        Host.Dispose();
    }
}

/// <summary>
/// A union appended as an event is stored as its active case, under the case's own event type name, and a
/// projection's <c>Apply(TUnion)</c> receives each case as the union.
/// </summary>
[BobcatFeature("Union events are stored as their case")]
public class union_event_specifications: CritterStackSpec, IClassFixture<UnionLedgerHost>
{
    public union_event_specifications(UnionLedgerHost app): base(app.Host)
    {
        ProjectionTimeout = 30.Seconds();
    }

    [Fact]
    public async Task an_appended_union_is_recorded_as_its_active_case()
    {
        var id = Guid.NewGuid();
        await GivenEvents<UnionLedger>(id, new LedgerOpened("bob"));

        await WhenActing("a fee is debited as a LedgerEntry",
            services => append(services, id, new LedgerEntry(new LedgerDebited(3m, "fee"))));

        ThenEvents(new LedgerDebited(3m, "fee"));
        await ThenReadModel<UnionLedger>(id, Specify<UnionLedger>()
            .With(x => x.Owner, "bob")
            .With(x => x.Balance, -3m)
            .With(x => x.Debits, 1));
    }

    [Fact]
    public async Task apply_of_the_union_projects_every_case()
    {
        var id = Guid.NewGuid();
        await GivenEvents<UnionLedger>(id,
            new LedgerOpened("ann"),
            new LedgerEntry(new LedgerCredited(10m)),
            new LedgerEntry(new LedgerDebited(3m, "fee")));

        await WhenActing("a deposit is credited as a LedgerEntry",
            services => append(services, id, new LedgerEntry(new LedgerCredited(5m))));

        ThenEvents(new LedgerCredited(5m));
        await ThenReadModel<UnionLedger>(id, Specify<UnionLedger>()
            .With(x => x.Owner, "ann")
            .With(x => x.Balance, 12m)
            .With(x => x.Debits, 1));
    }

    private static async Task append(IServiceProvider services, Guid id, object @event)
    {
        await using var session = services.GetRequiredService<IDocumentStore>().LightweightSession();
        session.Events.Append(id, @event);
        await session.SaveChangesAsync();
    }
}
