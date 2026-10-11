using System;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Aggregation;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace ContainerScopedProjectionTests;

public record ParcelSent(string Carrier);

public class ParcelSummary
{
    public Guid Id { get; set; }
    public string? Carrier { get; set; }
}

public class ParcelSummaryProjection: SingleStreamProjection<ParcelSummary, Guid>
{
    public ParcelSummaryProjection()
    {
        Backfill("carrier").On<ParcelSent>((patch, e) => patch.Set(x => x.Carrier, e.Carrier));
    }

    public ParcelSummary Create(IEvent<ParcelSent> e) => new() { Id = e.StreamId, Carrier = e.Data.Carrier };
}

[Collection("ioc")]
public class projection_backfills_and_service_lifetimes
{
    private static async Task<IHost> startAsync(ServiceLifetime lifetime)
    {
        var schema = $"backfill_{lifetime.ToString().ToLowerInvariant()}";
        await using (var conn = new NpgsqlConnection(ConnectionSource.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.DropSchemaAsync(schema);
        }

        return await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddMarten(opts =>
                    {
                        opts.Connection(ConnectionSource.ConnectionString);
                        opts.DatabaseSchemaName = schema;
                    })
                    .AddProjectionWithServices<ParcelSummaryProjection>(ProjectionLifecycle.Async, lifetime);
            })
            .StartAsync();
    }

    [Fact]
    public async Task a_singleton_registration_runs_its_backfills()
    {
        using var host = await startAsync(ServiceLifetime.Singleton);
        var store = (DocumentStore)host.Services.GetRequiredService<IDocumentStore>();

        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        await using (var session = store.LightweightSession())
        {
            session.Events.StartStream(new ParcelSent("PostNL"));
            await session.SaveChangesAsync();
        }

        using var daemon = await store.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();
        await daemon.WaitForNonStaleData(30.Seconds());

        (await store.Advanced.AllProjectionBackfills()).ShouldHaveSingleItem().IsCompleted.ShouldBeTrue();
    }

    [Fact]
    public async Task a_singleton_composite_member_registered_with_services_runs_its_backfills()
    {
        const string schema = "backfill_composite_member";
        await using (var conn = new NpgsqlConnection(ConnectionSource.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.DropSchemaAsync(schema);
        }

        using var host = await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddMarten(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DatabaseSchemaName = schema;
                    opts.Projections.CompositeProjectionFor("Parcels",
                        x => x.AddProjectionWithServices<ParcelSummaryProjection>(ServiceLifetime.Singleton));
                });
            })
            .StartAsync();

        var store = (DocumentStore)host.Services.GetRequiredService<IDocumentStore>();
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        await using (var session = store.LightweightSession())
        {
            session.Events.StartStream(new ParcelSent("DHL"));
            await session.SaveChangesAsync();
        }

        using var daemon = await store.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();
        await daemon.WaitForNonStaleData(30.Seconds());

        (await store.Advanced.AllProjectionBackfills()).ShouldHaveSingleItem().IsCompleted.ShouldBeTrue();
    }

    [Theory]
    [InlineData(ServiceLifetime.Scoped)]
    [InlineData(ServiceLifetime.Transient)]
    public async Task a_scoped_or_transient_registration_with_backfills_is_refused(ServiceLifetime lifetime)
    {
        var refusal = await Should.ThrowAsync<Exception>(async () =>
        {
            using var host = await startAsync(lifetime);
            host.Services.GetRequiredService<IDocumentStore>();
        });

        var exception = refusal;
        while (exception is not InvalidProjectionException && exception.InnerException != null)
        {
            exception = exception.InnerException;
        }

        exception.ShouldBeOfType<InvalidProjectionException>().Message.ShouldContain("declares backfills");
    }
}
