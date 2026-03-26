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
    #region Helpers

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

    private static StreamAction createSingleStream(EventGraph events, Guid id)
    {
        var questEvents = new object[]
        {
            new QuestStarted { Name = "Test Quest" },
            new MembersJoined(1, "Shire", "Frodo"),
            new MembersJoined(2, "Rivendell", "Aragorn"),
            new QuestEnded { Name = "Test Quest" }
        };
        return StreamAction.Start(events, id, questEvents);
    }

    private static StreamAction createSingleStringStream(EventGraph events, string key)
    {
        var questEvents = new object[]
        {
            new QuestStarted { Name = "Test Quest" },
            new MembersJoined(1, "Shire", "Bilbo"),
            new QuestEnded { Name = "Test Quest" }
        };
        return StreamAction.Start(events, key, questEvents);
    }

    #endregion

    #region Stream Identity: Guid

    [Fact]
    public async Task guid_identity_single_tenant()
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
        stats.EventCount.ShouldBe(80);

        var firstId = actions[0].Id;
        var events = await session.Events.FetchStreamAsync(firstId);
        events.Count.ShouldBe(4);

        // Verify event types are correct
        events[0].Data.ShouldBeOfType<QuestStarted>();
        events[1].Data.ShouldBeOfType<MembersJoined>();
        events[3].Data.ShouldBeOfType<QuestEnded>();
    }

    [Fact]
    public async Task guid_identity_conjoined_tenancy()
    {
        var store = StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsGuid;
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
        });

        var actions1 = createQuestStreams(store.Events, 10);
        await store.BulkInsertEventsAsync("tenant-1", actions1);

        var actions2 = createQuestStreams(store.Events, 5);
        await store.BulkInsertEventsAsync("tenant-2", actions2);

        await using var session1 = store.LightweightSession("tenant-1");
        var allEvents1 = await session1.Events.QueryAllRawEvents().ToListAsync();
        allEvents1.ShouldAllBe(e => e.TenantId == "tenant-1");
        allEvents1.Count.ShouldBe(40);

        await using var session2 = store.LightweightSession("tenant-2");
        var allEvents2 = await session2.Events.QueryAllRawEvents().ToListAsync();
        allEvents2.ShouldAllBe(e => e.TenantId == "tenant-2");
        allEvents2.Count.ShouldBe(20);

        // Verify streams are isolated per tenant
        var stream1 = await session1.Events.FetchStreamAsync(actions1[0].Id);
        stream1.Count.ShouldBe(4);

        var stream2CrossTenant = await session2.Events.FetchStreamAsync(actions1[0].Id);
        stream2CrossTenant.Count.ShouldBe(0);
    }

    #endregion

    #region Stream Identity: String

    [Fact]
    public async Task string_identity_single_tenant()
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
    public async Task string_identity_conjoined_tenancy()
    {
        var store = StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
        });

        var actions1 = createStringKeyedStreams(store.Events, 8);
        await store.BulkInsertEventsAsync("tenant-a", actions1);

        var actions2 = createStringKeyedStreams(store.Events, 4);
        await store.BulkInsertEventsAsync("tenant-b", actions2);

        await using var sessionA = store.LightweightSession("tenant-a");
        var eventsA = await sessionA.Events.QueryAllRawEvents().ToListAsync();
        eventsA.ShouldAllBe(e => e.TenantId == "tenant-a");
        eventsA.Count.ShouldBe(24); // 8 streams * 3 events

        await using var sessionB = store.LightweightSession("tenant-b");
        var eventsB = await sessionB.Events.QueryAllRawEvents().ToListAsync();
        eventsB.ShouldAllBe(e => e.TenantId == "tenant-b");
        eventsB.Count.ShouldBe(12);

        // Cross-tenant isolation
        var streamA = await sessionA.Events.FetchStreamAsync(actions1[0].Key!);
        streamA.Count.ShouldBe(3);

        var streamACrossTenant = await sessionB.Events.FetchStreamAsync(actions1[0].Key!);
        streamACrossTenant.Count.ShouldBe(0);
    }

    #endregion

    #region Metadata: Individual Columns

    [Fact]
    public async Task metadata_correlation_id_only()
    {
        var store = StoreOptions(opts =>
        {
            opts.Events.MetadataConfig.CorrelationIdEnabled = true;
        });

        var streamId = Guid.NewGuid();
        var action = createSingleStream(store.Events, streamId);
        for (int i = 0; i < action.Events.Count; i++)
        {
            action.Events[i].CorrelationId = $"corr-{i}";
        }

        await store.BulkInsertEventsAsync(new[] { action });

        await using var session = store.LightweightSession();
        var loaded = await session.Events.FetchStreamAsync(streamId);

        loaded.Count.ShouldBe(4);
        loaded[0].CorrelationId.ShouldBe("corr-0");
        loaded[1].CorrelationId.ShouldBe("corr-1");
        loaded[3].CorrelationId.ShouldBe("corr-3");
    }

    [Fact]
    public async Task metadata_causation_id_only()
    {
        var store = StoreOptions(opts =>
        {
            opts.Events.MetadataConfig.CausationIdEnabled = true;
        });

        var streamId = Guid.NewGuid();
        var action = createSingleStream(store.Events, streamId);
        for (int i = 0; i < action.Events.Count; i++)
        {
            action.Events[i].CausationId = $"cause-{i}";
        }

        await store.BulkInsertEventsAsync(new[] { action });

        await using var session = store.LightweightSession();
        var loaded = await session.Events.FetchStreamAsync(streamId);

        loaded[0].CausationId.ShouldBe("cause-0");
        loaded[2].CausationId.ShouldBe("cause-2");
    }

    [Fact]
    public async Task metadata_headers_only()
    {
        var store = StoreOptions(opts =>
        {
            opts.Events.MetadataConfig.HeadersEnabled = true;
        });

        var streamId = Guid.NewGuid();
        var action = createSingleStream(store.Events, streamId);
        action.Events[0].SetHeader("key-a", "value-a");
        action.Events[1].SetHeader("key-b", 42);

        await store.BulkInsertEventsAsync(new[] { action });

        await using var session = store.LightweightSession();
        var loaded = await session.Events.FetchStreamAsync(streamId);

        loaded[0].GetHeader("key-a").ShouldBe("value-a");
        loaded[1].GetHeader("key-b").ShouldNotBeNull();
    }

    [Fact]
    public async Task metadata_user_name_only()
    {
        var store = StoreOptions(opts =>
        {
            opts.Events.MetadataConfig.UserNameEnabled = true;
        });

        var streamId = Guid.NewGuid();
        var action = createSingleStream(store.Events, streamId);
        for (int i = 0; i < action.Events.Count; i++)
        {
            action.Events[i].UserName = $"user-{i}";
        }

        await store.BulkInsertEventsAsync(new[] { action });

        await using var session = store.LightweightSession();
        var loaded = await session.Events.FetchStreamAsync(streamId);

        loaded[0].UserName.ShouldBe("user-0");
        loaded[3].UserName.ShouldBe("user-3");
    }

    #endregion

    #region Metadata: All Columns Combined

    [Fact]
    public async Task metadata_all_columns_enabled()
    {
        var store = StoreOptions(opts =>
        {
            opts.Events.MetadataConfig.CorrelationIdEnabled = true;
            opts.Events.MetadataConfig.CausationIdEnabled = true;
            opts.Events.MetadataConfig.HeadersEnabled = true;
            opts.Events.MetadataConfig.UserNameEnabled = true;
        });

        var streamId = Guid.NewGuid();
        var action = createSingleStream(store.Events, streamId);
        for (int i = 0; i < action.Events.Count; i++)
        {
            action.Events[i].CorrelationId = $"corr-{i}";
            action.Events[i].CausationId = $"cause-{i}";
            action.Events[i].SetHeader("idx", i);
            action.Events[i].UserName = $"user-{i}";
        }

        await store.BulkInsertEventsAsync(new[] { action });

        await using var session = store.LightweightSession();
        var loaded = await session.Events.FetchStreamAsync(streamId);

        loaded.Count.ShouldBe(4);
        for (int i = 0; i < loaded.Count; i++)
        {
            loaded[i].CorrelationId.ShouldBe($"corr-{i}");
            loaded[i].CausationId.ShouldBe($"cause-{i}");
            loaded[i].GetHeader("idx").ShouldNotBeNull();
            loaded[i].UserName.ShouldBe($"user-{i}");
        }
    }

    [Fact]
    public async Task metadata_all_columns_with_nulls()
    {
        var store = StoreOptions(opts =>
        {
            opts.Events.MetadataConfig.CorrelationIdEnabled = true;
            opts.Events.MetadataConfig.CausationIdEnabled = true;
            opts.Events.MetadataConfig.HeadersEnabled = true;
            opts.Events.MetadataConfig.UserNameEnabled = true;
        });

        var streamId = Guid.NewGuid();
        var action = createSingleStream(store.Events, streamId);
        // Only set metadata on first event, leave rest null
        action.Events[0].CorrelationId = "corr-only";
        action.Events[0].CausationId = "cause-only";
        action.Events[0].UserName = "user-only";

        await store.BulkInsertEventsAsync(new[] { action });

        await using var session = store.LightweightSession();
        var loaded = await session.Events.FetchStreamAsync(streamId);

        loaded.Count.ShouldBe(4);
        loaded[0].CorrelationId.ShouldBe("corr-only");
        loaded[0].CausationId.ShouldBe("cause-only");
        loaded[0].UserName.ShouldBe("user-only");

        // Other events should have null metadata
        loaded[1].CorrelationId.ShouldBeNull();
        loaded[1].CausationId.ShouldBeNull();
        loaded[1].UserName.ShouldBeNull();
    }

    #endregion

    #region Metadata + String Identity

    [Fact]
    public async Task string_identity_with_all_metadata()
    {
        var store = StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Events.MetadataConfig.CorrelationIdEnabled = true;
            opts.Events.MetadataConfig.CausationIdEnabled = true;
            opts.Events.MetadataConfig.HeadersEnabled = true;
            opts.Events.MetadataConfig.UserNameEnabled = true;
        });

        var key = "meta-quest-string";
        var action = createSingleStringStream(store.Events, key);
        for (int i = 0; i < action.Events.Count; i++)
        {
            action.Events[i].CorrelationId = $"corr-{i}";
            action.Events[i].CausationId = $"cause-{i}";
            action.Events[i].UserName = $"user-{i}";
            action.Events[i].SetHeader("pos", i);
        }

        await store.BulkInsertEventsAsync(new[] { action });

        await using var session = store.LightweightSession();
        var loaded = await session.Events.FetchStreamAsync(key);

        loaded.Count.ShouldBe(3);
        loaded[0].CorrelationId.ShouldBe("corr-0");
        loaded[0].UserName.ShouldBe("user-0");
        loaded[2].CausationId.ShouldBe("cause-2");
    }

    #endregion

    #region Metadata + Conjoined Tenancy

    [Fact]
    public async Task conjoined_tenancy_with_all_metadata()
    {
        var store = StoreOptions(opts =>
        {
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.MetadataConfig.CorrelationIdEnabled = true;
            opts.Events.MetadataConfig.CausationIdEnabled = true;
            opts.Events.MetadataConfig.HeadersEnabled = true;
            opts.Events.MetadataConfig.UserNameEnabled = true;
        });

        var streamId = Guid.NewGuid();
        var action = createSingleStream(store.Events, streamId);
        for (int i = 0; i < action.Events.Count; i++)
        {
            action.Events[i].CorrelationId = $"tenant-corr-{i}";
            action.Events[i].CausationId = $"tenant-cause-{i}";
            action.Events[i].UserName = $"tenant-user-{i}";
        }

        await store.BulkInsertEventsAsync("my-tenant", new[] { action });

        await using var session = store.LightweightSession("my-tenant");
        var loaded = await session.Events.FetchStreamAsync(streamId);

        loaded.Count.ShouldBe(4);
        loaded.ShouldAllBe(e => e.TenantId == "my-tenant");
        loaded[0].CorrelationId.ShouldBe("tenant-corr-0");
        loaded[0].UserName.ShouldBe("tenant-user-0");
    }

    #endregion

    #region Archived Stream Partitioning Combos

    [Fact]
    public async Task archived_partitioning_with_guid_identity()
    {
        var store = StoreOptions(opts =>
        {
            opts.Events.UseArchivedStreamPartitioning = true;
        });

        var actions = createQuestStreams(store.Events, 10);
        await store.BulkInsertEventsAsync(actions);

        var stats = await store.Advanced.FetchEventStoreStatistics();
        stats.StreamCount.ShouldBe(10);
        stats.EventCount.ShouldBe(40);
    }

    [Fact]
    public async Task archived_partitioning_with_string_identity()
    {
        var store = StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Events.UseArchivedStreamPartitioning = true;
        });

        var actions = createStringKeyedStreams(store.Events, 10);
        await store.BulkInsertEventsAsync(actions);

        var stats = await store.Advanced.FetchEventStoreStatistics();
        stats.StreamCount.ShouldBe(10);
        stats.EventCount.ShouldBe(30);
    }

    [Fact]
    public async Task archived_partitioning_with_conjoined_tenancy()
    {
        var store = StoreOptions(opts =>
        {
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.UseArchivedStreamPartitioning = true;
        });

        var actions = createQuestStreams(store.Events, 8);
        await store.BulkInsertEventsAsync("partitioned-tenant", actions);

        await using var session = store.LightweightSession("partitioned-tenant");
        var events = await session.Events.QueryAllRawEvents().ToListAsync();
        events.Count.ShouldBe(32);
        events.ShouldAllBe(e => e.TenantId == "partitioned-tenant");
    }

    [Fact]
    public async Task archived_partitioning_with_conjoined_tenancy_and_string_identity()
    {
        var store = StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.UseArchivedStreamPartitioning = true;
        });

        var actions = createStringKeyedStreams(store.Events, 6);
        await store.BulkInsertEventsAsync("combined-tenant", actions);

        await using var session = store.LightweightSession("combined-tenant");
        var events = await session.Events.QueryAllRawEvents().ToListAsync();
        events.Count.ShouldBe(18);
        events.ShouldAllBe(e => e.TenantId == "combined-tenant");

        var stream = await session.Events.FetchStreamAsync(actions[0].Key!);
        stream.Count.ShouldBe(3);
    }

    #endregion

    #region Version and Sequence Correctness

    [Fact]
    public async Task versions_correct_with_guid_identity()
    {
        var store = StoreOptions(opts => { });

        var streamId = Guid.NewGuid();
        var action = createSingleStream(store.Events, streamId);
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
    public async Task versions_correct_with_string_identity()
    {
        var store = StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsString;
        });

        var key = "version-test-stream";
        var action = createSingleStringStream(store.Events, key);
        await store.BulkInsertEventsAsync(new[] { action });

        await using var session = store.LightweightSession();
        var loaded = await session.Events.FetchStreamAsync(key);

        for (int i = 0; i < loaded.Count; i++)
        {
            loaded[i].Version.ShouldBe(i + 1);
        }

        var state = await session.Events.FetchStreamStateAsync(key);
        state.ShouldNotBeNull();
        state!.Version.ShouldBe(3);
    }

    [Fact]
    public async Task sequences_are_globally_unique_and_ordered()
    {
        var store = StoreOptions(opts => { });

        var actions = createQuestStreams(store.Events, 20);
        await store.BulkInsertEventsAsync(actions);

        await using var session = store.LightweightSession();
        var allEvents = await session.Events.QueryAllRawEvents()
            .OrderBy(e => e.Sequence)
            .ToListAsync();

        // All sequences should be unique
        var sequences = allEvents.Select(e => e.Sequence).ToList();
        sequences.Distinct().Count().ShouldBe(sequences.Count);

        // Should be in ascending order
        for (int i = 1; i < sequences.Count; i++)
        {
            sequences[i].ShouldBeGreaterThan(sequences[i - 1]);
        }
    }

    [Fact]
    public async Task high_water_mark_updated()
    {
        var store = StoreOptions(opts => { });

        var actions = createQuestStreams(store.Events, 10);
        var totalEvents = actions.Sum(a => a.Events.Count);

        await store.BulkInsertEventsAsync(actions);

        var stats = await store.Advanced.FetchEventStoreStatistics();
        stats.EventCount.ShouldBe(totalEvents);
    }

    #endregion

    #region Batching

    [Fact]
    public async Task multiple_copy_batches()
    {
        var store = StoreOptions(opts => { });

        var actions = createQuestStreams(store.Events, 50);
        await store.BulkInsertEventsAsync(actions, batchSize: 10);

        var stats = await store.Advanced.FetchEventStoreStatistics();
        stats.StreamCount.ShouldBe(50);
        stats.EventCount.ShouldBe(200);
    }

    [Fact]
    public async Task batch_size_of_one()
    {
        var store = StoreOptions(opts => { });

        var actions = createQuestStreams(store.Events, 5);
        await store.BulkInsertEventsAsync(actions, batchSize: 1);

        var stats = await store.Advanced.FetchEventStoreStatistics();
        stats.StreamCount.ShouldBe(5);
        stats.EventCount.ShouldBe(20);
    }

    #endregion

    #region Full Combination: Conjoined + String + All Metadata + Archived Partitioning

    [Fact]
    public async Task full_combination_all_options_enabled()
    {
        var store = StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.UseArchivedStreamPartitioning = true;
            opts.Events.MetadataConfig.CorrelationIdEnabled = true;
            opts.Events.MetadataConfig.CausationIdEnabled = true;
            opts.Events.MetadataConfig.HeadersEnabled = true;
            opts.Events.MetadataConfig.UserNameEnabled = true;
        });

        var key = "full-combo-quest";
        var action = createSingleStringStream(store.Events, key);
        for (int i = 0; i < action.Events.Count; i++)
        {
            action.Events[i].CorrelationId = $"fc-corr-{i}";
            action.Events[i].CausationId = $"fc-cause-{i}";
            action.Events[i].UserName = $"fc-user-{i}";
            action.Events[i].SetHeader("full-combo", true);
        }

        await store.BulkInsertEventsAsync("full-combo-tenant", new[] { action });

        await using var session = store.LightweightSession("full-combo-tenant");
        var loaded = await session.Events.FetchStreamAsync(key);

        loaded.Count.ShouldBe(3);
        loaded.ShouldAllBe(e => e.TenantId == "full-combo-tenant");

        for (int i = 0; i < loaded.Count; i++)
        {
            loaded[i].Version.ShouldBe(i + 1);
            loaded[i].CorrelationId.ShouldBe($"fc-corr-{i}");
            loaded[i].CausationId.ShouldBe($"fc-cause-{i}");
            loaded[i].UserName.ShouldBe($"fc-user-{i}");
            loaded[i].GetHeader("full-combo").ShouldNotBeNull();
        }

        var state = await session.Events.FetchStreamStateAsync(key);
        state.ShouldNotBeNull();
        state!.Version.ShouldBe(3);
    }

    #endregion
}
