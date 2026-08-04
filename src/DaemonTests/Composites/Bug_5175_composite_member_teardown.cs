using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Projections;
using JasperFx.Events.Projections.Composite;
using Marten;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace DaemonTests.Composites;

/// <summary>
/// #5175 — a composite's rebuild tears its members down by reading each member's own
/// <c>Options.CleanUps</c>. Two of the four registration paths wrap the projection in a source with a
/// fresh, EMPTY <c>AsyncOptions</c>, so those members contributed no teardown at all: the rebuild
/// restarted from sequence zero against a table still holding the previous run's documents. The
/// composite's own options were never applied either.
/// </summary>
public class Bug_5175_composite_member_teardown: HostedStoreContext
{
    private const string CompositeName = "Teardown5175";

    private async Task<IDocumentStore> startAsync()
    {
        var host = await StartHostAsync(opts =>
            {
                opts.Projections.CompositeProjectionFor(CompositeName, projection =>
                {
                    // Path 3 in the issue's table: the source is a CompositeProjectionWithServicesSource<T>
                    // whose options used to stay empty.
                    projection.AddProjectionWithServices<Teardown5175ProductProjection>(ServiceLifetime.Scoped);

                    // Path 4: a raw IProjection wrapped in CompositeIProjectionSource. It declares nothing
                    // about its own storage, so the teardown rule is declared at registration.
                    projection.Add(new Teardown5175MetricProjection(),
                        options => options.DeleteViewTypeOnTeardown<Teardown5175Metric>());
                });
            },
            configureServices: services => services.AddScoped<ITeardown5175Pricing, Teardown5175Pricing>(),
            configureMarten: marten => marten.ApplyAllDatabaseChangesOnStartup());

        var store = host.Services.GetRequiredService<IDocumentStore>();
        await store.Advanced.Clean.CompletelyRemoveAllAsync(TestContext.Current.CancellationToken);
        return store;
    }

    private static CompositeProjection compositeFor(IDocumentStore store) =>
        ((DocumentStore)store).Options.Projections.All.OfType<CompositeProjection>().Single(x => x.Name == CompositeName);

    [Fact]
    public async Task every_member_reports_its_own_teardown_rules()
    {
        var store = await startAsync();

        var cleanupTypes = compositeFor(store).AllProjections()
            .SelectMany(x => x.Options.CleanUps)
            .OfType<DeleteDocuments>()
            .Select(x => x.DocumentType)
            .ToArray();

        // Before #5175 both of these were absent — the wrappers carried an empty AsyncOptions.
        cleanupTypes.ShouldContain(typeof(Teardown5175Product));
        cleanupTypes.ShouldContain(typeof(Teardown5175Metric));
    }

    [Fact]
    public async Task rebuilding_deletes_every_members_documents_first()
    {
        var store = await startAsync();

        await using (var session = store.LightweightSession())
        {
            session.Events.StartStream<Teardown5175Product>(
                new Teardown5175Registered("Ankle Socks", "Socks"));
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using (var daemon = await store.BuildProjectionDaemonAsync())
        {
            await daemon.StartAllAsync();
            await daemon.WaitForNonStaleData(30.Seconds());
            await daemon.StopAllAsync();
        }

        // Orphans: rows of each member's view type that NO event can reproduce. If the rebuild's teardown
        // reaches every member, they are gone afterwards; if a member contributes no teardown, its orphan
        // survives and the rebuild replays into a table that still holds the previous run's documents.
        var orphanId = Guid.NewGuid();
        await using (var session = store.LightweightSession())
        {
            session.Store(new Teardown5175Product { Id = orphanId, Name = "Ghost", Category = "Ghost" });
            session.Store(new Teardown5175Metric { Id = orphanId, Price = 999 });
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using (var daemon = await store.BuildProjectionDaemonAsync())
        {
            await daemon.RebuildProjectionAsync(CompositeName, 60.Seconds(), TestContext.Current.CancellationToken);
        }

        await using var query = store.QuerySession();
        (await query.LoadAsync<Teardown5175Product>(orphanId, TestContext.Current.CancellationToken))
            .ShouldBeNull("the AddProjectionWithServices member's documents must be torn down on rebuild");
        (await query.LoadAsync<Teardown5175Metric>(orphanId, TestContext.Current.CancellationToken))
            .ShouldBeNull("the raw IProjection member's documents must be torn down on rebuild");

        // ...and the rebuild genuinely reproduced the real read models it deleted.
        (await query.Query<Teardown5175Product>().CountAsync(TestContext.Current.CancellationToken)).ShouldBe(1);
        (await query.Query<Teardown5175Metric>().CountAsync(TestContext.Current.CancellationToken)).ShouldBe(1);
    }
}

public interface ITeardown5175Pricing
{
    double PriceFor(string category);
}

public class Teardown5175Pricing: ITeardown5175Pricing
{
    public double PriceFor(string category) => category == "Socks" ? 12.5 : 5;
}

public record Teardown5175Registered(string Name, string Category);

public class Teardown5175Product
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string Category { get; set; }
    public double Price { get; set; }
}

public class Teardown5175Metric
{
    public Guid Id { get; set; }
    public double Price { get; set; }
}

public partial class Teardown5175ProductProjection: SingleStreamProjection<Teardown5175Product, Guid>
{
    private readonly ITeardown5175Pricing _pricing;

    public Teardown5175ProductProjection(ITeardown5175Pricing pricing)
    {
        _pricing = pricing;
        Name = "Teardown5175Product";
    }

    public override Teardown5175Product Evolve(Teardown5175Product snapshot, Guid id, IEvent e)
    {
        snapshot ??= new Teardown5175Product { Id = id };

        if (e.Data is Teardown5175Registered registered)
        {
            snapshot.Name = registered.Name;
            snapshot.Category = registered.Category;
            snapshot.Price = _pricing.PriceFor(registered.Category);
        }

        return snapshot;
    }
}

public class Teardown5175MetricProjection: IProjection
{
    public Task ApplyAsync(IDocumentOperations operations, IReadOnlyList<IEvent> events,
        CancellationToken cancellation)
    {
        foreach (var e in events)
        {
            if (e.Data is Teardown5175Registered)
            {
                operations.Store(new Teardown5175Metric { Id = e.StreamId, Price = 1 });
            }
        }

        return Task.CompletedTask;
    }
}
