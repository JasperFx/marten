using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace EventSourcingTests;

// jasperfx#420 / marten#4710: resolution of the per-database rebuild concurrency cap
// surfaced through IEventStore.MaxConcurrentRebuildsPerDatabase.
public class rebuild_concurrency_cap_resolution
{
    private static DocumentStore storeWith(Action<StoreOptions> configure)
    {
        return DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "rebuild_cap";
            configure(opts);
        });
    }

    [Fact]
    public void configured_value_wins_over_derived_default()
    {
        using var store = storeWith(opts => opts.Projections.MaxConcurrentRebuildsPerDatabase = 3);
        ((IEventStore)store).MaxConcurrentRebuildsPerDatabase.ShouldBe(3);
    }

    [Fact]
    public void non_positive_configured_value_disables_the_cap()
    {
        using var store = storeWith(opts => opts.Projections.MaxConcurrentRebuildsPerDatabase = 0);
        ((IEventStore)store).MaxConcurrentRebuildsPerDatabase.ShouldBeNull();
    }

    [Fact]
    public void derived_default_is_pool_size_over_eight_with_floor_of_one()
    {
        var connectionString = new NpgsqlConnectionStringBuilder(ConnectionSource.ConnectionString)
        {
            MaxPoolSize = 64
        }.ConnectionString;

        using var store = DocumentStore.For(opts =>
        {
            opts.Connection(connectionString);
            opts.DatabaseSchemaName = "rebuild_cap";
        });

        ((IEventStore)store).MaxConcurrentRebuildsPerDatabase.ShouldBe(8);
    }

    [Fact]
    public void derived_default_floors_at_one_for_tiny_pools()
    {
        var connectionString = new NpgsqlConnectionStringBuilder(ConnectionSource.ConnectionString)
        {
            MaxPoolSize = 5
        }.ConnectionString;

        using var store = DocumentStore.For(opts =>
        {
            opts.Connection(connectionString);
            opts.DatabaseSchemaName = "rebuild_cap";
        });

        ((IEventStore)store).MaxConcurrentRebuildsPerDatabase.ShouldBe(1);
    }

    [Fact]
    public async Task usage_descriptor_carries_the_effective_cap()
    {
        // jasperfx#434: CritterWatch#309's rebuild dispatcher reads the effective cap
        // off the EventStoreUsage descriptor rather than guessing.
        using var store = storeWith(opts => opts.Projections.MaxConcurrentRebuildsPerDatabase = 6);

        var usage = await ((IEventStore)store).TryCreateUsage(CancellationToken.None);

        usage.ShouldNotBeNull();
        usage.MaxConcurrentRebuildsPerDatabase.ShouldBe(6);
    }
}
