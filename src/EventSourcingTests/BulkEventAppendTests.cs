using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten;
using Marten.Events;
using Marten.Storage;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests;

public class BulkEventAppendTests: OneOffConfigurationsContext
{
    private static List<StreamAction> createQuestStreams(EventGraph events, int count)
    {
        var actions = new List<StreamAction>();
        for (int i = 0; i < count; i++)
        {
            var id = Guid.NewGuid();
            var questEvents = new object[]
            {
                new QuestStarted { Name = $"Quest {i}" },
                new MembersJoined(1, "Somewhere", "Frodo", "Sam"),
                new MembersJoined(2, "Rivendell", "Aragorn"),
                new QuestEnded { Name = $"Quest {i}" }
            };

            actions.Add(StreamAction.Start(events, id, questEvents));
        }

        return actions;
    }

    private static List<StreamAction> createStringKeyedStreams(EventGraph events, int count)
    {
        var actions = new List<StreamAction>();
        for (int i = 0; i < count; i++)
        {
            var key = $"quest-{Guid.NewGuid():N}";
            var questEvents = new object[]
            {
                new QuestStarted { Name = $"Quest {i}" },
                new MembersJoined(1, "Shire", "Bilbo"),
                new QuestEnded { Name = $"Quest {i}" }
            };

            actions.Add(StreamAction.Start(events, key, questEvents));
        }

        return actions;
    }

    [Fact]
    public async Task can_bulk_insert_events_with_guid_identity()
    {
        var store = StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsGuid;
        });

        var actions = createQuestStreams(store.Events, 20);
        await store.BulkInsertEventsAsync(actions);

        await using var session = store.LightweightSession();
        var stats = await store.Advanced.FetchEventStoreStatistics();

        stats.StreamCount.ShouldBe(20);
        stats.EventCount.ShouldBe(80); // 4 events per stream

        // Verify a specific stream can be loaded
        var firstId = actions[0].Id;
        var events = await session.Events.FetchStreamAsync(firstId);
        events.Count.ShouldBe(4);
    }

    [Fact]
    public async Task can_bulk_insert_events_with_string_identity()
    {
        var store = StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsString;
        });

        var actions = createStringKeyedStreams(store.Events, 10);
        await store.BulkInsertEventsAsync(actions);

        await using var session = store.LightweightSession();
        var stats = await store.Advanced.FetchEventStoreStatistics();
        stats.StreamCount.ShouldBe(10);

        var firstKey = actions[0].Key;
        var events = await session.Events.FetchStreamAsync(firstKey!);
        events.Count.ShouldBe(3);
    }

    [Fact]
    public async Task can_bulk_insert_events_with_conjoined_tenancy()
    {
        var store = StoreOptions(opts =>
        {
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
        });

        var actions1 = createQuestStreams(store.Events, 10);
        await store.BulkInsertEventsAsync("tenant-1", actions1);

        var actions2 = createQuestStreams(store.Events, 5);
        await store.BulkInsertEventsAsync("tenant-2", actions2);

        // Verify tenant isolation
        await using var session1 = store.LightweightSession("tenant-1");
        var allEvents1 = await session1.Events.QueryAllRawEvents().ToListAsync();
        allEvents1.ShouldAllBe(e => e.TenantId == "tenant-1");
        allEvents1.Count.ShouldBe(40);

        await using var session2 = store.LightweightSession("tenant-2");
        var allEvents2 = await session2.Events.QueryAllRawEvents().ToListAsync();
        allEvents2.ShouldAllBe(e => e.TenantId == "tenant-2");
        allEvents2.Count.ShouldBe(20);
    }

    [Fact]
    public async Task can_bulk_insert_events_with_metadata()
    {
        var store = StoreOptions(opts =>
        {
            opts.Events.MetadataConfig.CorrelationIdEnabled = true;
            opts.Events.MetadataConfig.CausationIdEnabled = true;
            opts.Events.MetadataConfig.HeadersEnabled = true;
        });

        var streamId = Guid.NewGuid();
        var questEvents = new object[]
        {
            new QuestStarted { Name = "Meta Quest" },
            new MembersJoined(1, "Shire", "Frodo"),
            new QuestEnded { Name = "Meta Quest" }
        };

        var action = StreamAction.Start(store.Events, streamId, questEvents);

        for (int i = 0; i < action.Events.Count; i++)
        {
            action.Events[i].CorrelationId = $"corr-{i}";
            action.Events[i].CausationId = $"cause-{i}";
            action.Events[i].SetHeader("test-key", $"value-{i}");
        }

        await store.BulkInsertEventsAsync(new[] { action });

        await using var session = store.LightweightSession();
        var loaded = await session.Events.FetchStreamAsync(streamId);

        loaded.Count.ShouldBe(3);
        loaded[0].CorrelationId.ShouldBe("corr-0");
        loaded[0].CausationId.ShouldBe("cause-0");
        loaded[0].GetHeader("test-key").ShouldBe("value-0");

        loaded[2].CorrelationId.ShouldBe("corr-2");
        loaded[2].CausationId.ShouldBe("cause-2");
    }

    [Fact]
    public async Task bulk_insert_sets_correct_versions()
    {
        var store = StoreOptions(opts => { });

        var streamId = Guid.NewGuid();
        var questEvents = new object[]
        {
            new QuestStarted { Name = "Version Quest" },
            new MembersJoined(1, "Shire", "Frodo"),
            new MembersJoined(2, "Rivendell", "Aragorn"),
            new QuestEnded { Name = "Version Quest" }
        };

        var action = StreamAction.Start(store.Events, streamId, questEvents);
        await store.BulkInsertEventsAsync(new[] { action });

        await using var session = store.LightweightSession();
        var loaded = await session.Events.FetchStreamAsync(streamId);

        for (int i = 0; i < loaded.Count; i++)
        {
            loaded[i].Version.ShouldBe(i + 1);
        }

        var state = await session.Events.FetchStreamStateAsync(streamId);
        state.ShouldNotBeNull();
        state!.Version.ShouldBe(4);
    }

    [Fact]
    public async Task bulk_insert_updates_high_water_mark()
    {
        var store = StoreOptions(opts => { });

        var actions = createQuestStreams(store.Events, 10);
        var totalEvents = actions.Sum(a => a.Events.Count);

        await store.BulkInsertEventsAsync(actions);

        var stats = await store.Advanced.FetchEventStoreStatistics();
        stats.EventCount.ShouldBe(totalEvents);
    }

    [Fact]
    public async Task can_bulk_insert_with_archived_stream_partitioning()
    {
        var store = StoreOptions(opts =>
        {
            opts.Events.UseArchivedStreamPartitioning = true;
        });

        var actions = createQuestStreams(store.Events, 10);
        await store.BulkInsertEventsAsync(actions);

        await using var session = store.LightweightSession();
        var stats = await store.Advanced.FetchEventStoreStatistics();
        stats.StreamCount.ShouldBe(10);
        stats.EventCount.ShouldBe(40);
    }

    [Fact]
    public async Task can_bulk_insert_multiple_batches()
    {
        var store = StoreOptions(opts => { });

        // Use a very small batch size to force multiple COPY operations
        var actions = createQuestStreams(store.Events, 50);
        await store.BulkInsertEventsAsync(actions, batchSize: 10);

        var stats = await store.Advanced.FetchEventStoreStatistics();
        stats.StreamCount.ShouldBe(50);
        stats.EventCount.ShouldBe(200);
    }
}
