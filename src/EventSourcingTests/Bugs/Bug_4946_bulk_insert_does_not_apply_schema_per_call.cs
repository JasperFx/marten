using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using Marten;
using Marten.Events;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Bugs;

/// <summary>
/// GH-4946: the batch <c>BulkInsertEventsAsync</c> overloads ran
/// <c>Storage.ApplyAllConfiguredChangesToDatabaseAsync()</c> on *every* call, so a conversion tool
/// importing in 1,000-event batches paid a full schema delta — partition introspection plus
/// information_schema sweeps, across every database in the store — per batch. The regression is
/// *how many times* the apply runs, so that is what these specs assert.
/// </summary>
public class Bug_4946_bulk_insert_does_not_apply_schema_per_call: OneOffConfigurationsContext
{
    private static List<StreamAction> streams(EventGraph events, int count)
    {
        var actions = new List<StreamAction>();
        for (var i = 0; i < count; i++)
        {
            actions.Add(StreamAction.Start(events, Guid.NewGuid(), new object[]
            {
                new QuestStarted { Name = $"Quest {i}" }, new QuestEnded { Name = $"Quest {i}" }
            }));
        }

        return actions;
    }

    [Fact]
    public async Task apply_the_schema_at_most_once_across_many_batches()
    {
        var store = StoreOptions(opts => opts.Events.StreamIdentity = StreamIdentity.AsGuid);

        for (var i = 0; i < 5; i++)
        {
            await store.BulkInsertEventsAsync(streams(store.Events, 2));
        }

        store.BulkInsertSchemaApplicationCount.ShouldBe(1);

        var stats = await store.Advanced.FetchEventStoreStatistics();
        stats.EventCount.ShouldBe(20);
    }

    [Fact]
    public async Task apply_the_schema_at_most_once_across_many_batches_for_a_tenant()
    {
        var store = StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsGuid;
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
        });

        for (var i = 0; i < 5; i++)
        {
            await store.BulkInsertEventsAsync("tenant-one", streams(store.Events, 2));
        }

        // Same single database behind every tenant here, so exactly one apply
        store.BulkInsertSchemaApplicationCount.ShouldBe(1);
    }

    [Fact]
    public async Task concurrent_batches_only_apply_the_schema_once()
    {
        var store = StoreOptions(opts => opts.Events.StreamIdentity = StreamIdentity.AsGuid);

        var batches = Enumerable.Range(0, 10)
            .Select(_ => store.BulkInsertEventsAsync(streams(store.Events, 2)))
            .ToArray();

        await Task.WhenAll(batches);

        store.BulkInsertSchemaApplicationCount.ShouldBe(1);
    }

    [Fact]
    public async Task no_schema_apply_at_all_when_auto_create_is_none()
    {
        // Get the schema in place with a "normal" store first
        var creator = StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsGuid;
            opts.Events.AddEventTypes([typeof(QuestStarted), typeof(QuestEnded)]);
        });

        await creator.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        // Same schema, but the schema is managed out of band now
        var store = SeparateStore(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsGuid;
            opts.AutoCreateSchemaObjects = AutoCreate.None;
        });

        for (var i = 0; i < 3; i++)
        {
            await store.BulkInsertEventsAsync(streams(store.Events, 2));
        }

        // The apply is a no-op by contract under AutoCreate.None, so it should never be attempted
        store.BulkInsertSchemaApplicationCount.ShouldBe(0);

        var stats = await store.Advanced.FetchEventStoreStatistics();
        stats.EventCount.ShouldBe(12);
    }
}
