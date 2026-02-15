using System;
using System.Linq;
using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using EventSourcingTests.FetchForWriting;
using JasperFx.Events;
using Marten;
using Marten.Events;
using Marten.Events.Schema;
using Marten.Testing;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace EventSourcingTests;

public class marking_events_as_skipped_as_Guid_identified : OneOffConfigurationsContext, IAsyncLifetime
{
    private Guid theStreamId;

    public marking_events_as_skipped_as_Guid_identified()
    {
        StoreOptions(opts =>
        {
            opts.Events.EnableEventSkippingInProjectionsOrSubscriptions = true;
        });
    }

    public async Task InitializeAsync()
    {
        theStreamId = theSession.Events.StartStream<SimpleAggregate>(new AEvent(), new BEvent(), new BEvent(), new CEvent(), new CEvent(), new CEvent()).Id;
        await theSession.SaveChangesAsync();

        var aggregate1 = await theSession.Events.AggregateStreamAsync<SimpleAggregate>(theStreamId);

        // CEvent have not been skipped
        aggregate1.CCount.ShouldBe(3);

        var events = await theSession.Events.QueryAllRawEvents().Where(x => x.EventTypesAre(typeof(CEvent)))
            .ToListAsync();

        events.Any().ShouldBeTrue();

        var sequences = events.Select(x => x.Sequence).ToArray();

        await theStore.Storage.Database.MarkEventsAsSkipped(sequences);
    }

    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    [Fact]
    public async Task has_the_is_skipped_column()
    {
        var table = new EventsTable(theStore.Events);
        var column = table.ColumnFor("is_skipped");
        column.ShouldNotBeNull();

        await theStore.EnsureStorageExistsAsync(typeof(IEvent));

        using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();

        var existing = await table.FetchExistingAsync(conn);
        await conn.CloseAsync();

        existing.ColumnFor("is_skipped").ShouldNotBeNull();
    }

    /*
     * TODO -- test it with...
     * Async + FetchLatest
     * Async + FetchForWriting
     *
     * Do everything as Guid v. string identified
     */

    [Fact]
    public async Task use_skip_for_realsies_and_live_aggregation()
    {
        var aggregate2 = await theSession.Events.AggregateStreamAsync<SimpleAggregate>(theStreamId);

        // All the CCount were skipped
        aggregate2.CCount.ShouldBe(0);
    }

    [Fact]
    public async Task applies_to_fetch_latest_as_live()
    {
        var aggregate2 = await theSession.Events.FetchLatest<SimpleAggregate>(theStreamId);

        // All the CCount were skipped
        aggregate2.CCount.ShouldBe(0);
    }

    [Fact]
    public async Task applies_to_fetch_fetch_for_writing_as_live()
    {
        var aggregate2 = await theSession.Events.FetchForWriting<SimpleAggregate>(theStreamId);

        // All the CCount were skipped
        aggregate2.Aggregate.CCount.ShouldBe(0);
    }



}
