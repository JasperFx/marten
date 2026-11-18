using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.MultiTenancy;
using Marten;
using Marten.Events.Rewriting;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace EventSourcingTests.Rewriting;

/// <summary>
///     Writes a fresh prefix in front of the JSON, the way an encrypting serializer with a random IV never writes
///     the same bytes twice for the same event.
/// </summary>
public class RandomPrefixBinarySerializer: JasperFx.Events.IEventBinarySerializer
{
    // A counter rather than a random byte: one random byte repeats one time in 256, which made a rewrite of the
    // same event write the same bytes and count as unchanged now and then
    private static int _writes;

    public byte[] Serialize(Type type, object data)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(data, type);
        var bytes = new byte[json.Length + sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, Interlocked.Increment(ref _writes));
        json.CopyTo(bytes, sizeof(int));
        return bytes;
    }

    public object Deserialize(Type type, byte[] data) => JsonSerializer.Deserialize(data.AsSpan(sizeof(int)), type)!;
}

public class choosing_what_a_rewrite_touches: OneOffConfigurationsContext
{
    public record PersonRegisteredV1(string Name);

    public record PersonRegistered(string FullName);

    public record PersonMoved(string City);

    private void withUpcaster(Action<StoreOptions>? configure = null) => StoreOptions(opts =>
    {
        opts.Events.Upcast<PersonRegisteredV1, PersonRegistered>(x => new PersonRegistered(x.Name));
        opts.Events.AddEventType<PersonMoved>();
        configure?.Invoke(opts);
    });

    private async Task<Guid> appendAsync(DocumentStore store, params object[] events)
    {
        var streamId = Guid.NewGuid();
        await using var session = store.LightweightSession();
        session.Events.StartStream(streamId, events);
        await session.SaveChangesAsync();
        return streamId;
    }

    private async Task<List<(string Type, string Tenant, bool Binary)>> storedRowsAsync()
    {
        await using var conn = theStore.Storage.Database.CreateConnection();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            $"select type, tenant_id, bdata is not null from {SchemaName}.mt_events order by seq_id", conn);
        await using var reader = await cmd.ExecuteReaderAsync();

        var rows = new List<(string, string, bool)>();
        while (await reader.ReadAsync()) rows.Add((reader.GetString(0), reader.GetString(1), reader.GetBoolean(2)));
        return rows;
    }

    [Fact]
    public async Task only_events_still_stored_as_json_are_read_when_the_serializer_never_writes_the_same_bytes_twice()
    {
        StoreOptions(opts => opts.Events.AddEventType<PersonMoved>());
        var streamId = await appendAsync(theStore, new PersonMoved("Utrecht"), new PersonMoved("Leiden"));

        using var binary = SeparateStore(opts =>
            opts.Events.UseBinarySerializer<PersonMoved>(new RandomPrefixBinarySerializer()));

        var first = await binary.Advanced.RewriteStoredEventsAsync(x =>
            x.IncludeType<PersonMoved>().OnlyEventsStoredAsJson());
        first.Databases.Single().Rewritten.ShouldBe(2);
        (await storedRowsAsync()).ShouldAllBe(x => x.Binary);

        // Already in bdata, so not even read
        var second = await binary.Advanced.RewriteStoredEventsAsync(x =>
            x.IncludeType<PersonMoved>().OnlyEventsStoredAsJson());
        second.Databases.Single().Visited.ShouldBe(0);
        second.Databases.Single().Rewritten.ShouldBe(0);

        // What the filter saves: without it, every row comes out different and is written again
        var withoutFilter = await binary.Advanced.RewriteStoredEventsAsync(x => x.IncludeType<PersonMoved>());
        withoutFilter.Databases.Single().Rewritten.ShouldBe(2);

        await using var query = binary.QuerySession();
        var events = await query.Events.FetchStreamAsync(streamId);
        events.Select(x => x.Data).ShouldBe([new PersonMoved("Utrecht"), new PersonMoved("Leiden")]);
    }

    [Fact]
    public async Task one_tenant_of_a_conjoined_store_is_rewritten_alone()
    {
        withUpcaster(opts =>
        {
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Policies.AllDocumentsAreMultiTenanted();
        });

        foreach (var tenant in new[] { "red", "blue" })
        {
            await using var session = theStore.LightweightSession(tenant);
            session.Events.StartStream(Guid.NewGuid(), new PersonRegisteredV1($"{tenant} 1"),
                new PersonRegisteredV1($"{tenant} 2"));
            await session.SaveChangesAsync();
        }

        var result = await theStore.Advanced.RewriteStoredEventsAsync(x =>
        {
            x.IncludeType<PersonRegistered>();
            x.ForTenant("blue");
            x.BatchSize = 1;
        });

        result.Databases.Single().Visited.ShouldBe(2);
        result.Databases.Single().Rewritten.ShouldBe(2);

        var rows = await storedRowsAsync();
        rows.Where(x => x.Tenant == "blue").ShouldAllBe(x => x.Type == "person_registered");
        rows.Where(x => x.Tenant == "red").ShouldAllBe(x => x.Type == "person_registered_v1");
    }

    [Fact]
    public async Task one_database_is_chosen_by_its_identifier()
    {
        withUpcaster();
        await appendAsync(theStore, new PersonRegisteredV1("Alice"));

        var result = await theStore.Advanced.RewriteStoredEventsAsync(x => x
            .IncludeType<PersonRegistered>()
            .ForDatabase(theStore.Storage.Database.Identifier));

        result.Databases.Single().Cursor.Database.ShouldBe(theStore.Storage.Database.Identifier);
        result.Databases.Single().Rewritten.ShouldBe(1);

        await Should.ThrowAsync<ArgumentOutOfRangeException>(() =>
            theStore.Advanced.RewriteStoredEventsAsync(x => x.IncludeType<PersonRegistered>().ForDatabase("nowhere")));
    }

    [Fact]
    public async Task events_of_a_type_the_application_no_longer_knows_are_left_alone()
    {
        withUpcaster();
        var streamId = await appendAsync(theStore, new PersonRegisteredV1("Alice"), new PersonMoved("Utrecht"),
            new PersonRegisteredV1("Bob"));

        // The second event's type has since been deleted from the code
        await using (var conn = theStore.Storage.Database.CreateConnection())
        {
            await conn.OpenAsync();
            await conn.CreateCommand(
                    $"update {SchemaName}.mt_events set type = 'retired_event', mt_dotnet_type = 'Nowhere.Retired, Nowhere' " +
                    "where type = 'person_moved'")
                .ExecuteNonQueryAsync();
        }

        // Two per page, so the unknown event is the last row of the first page
        var result = await theStore.Advanced.RewriteStoredEventsAsync(x =>
        {
            x.IncludeAllTypes();
            x.BatchSize = 2;
        });

        var database = result.Databases.Single();
        database.Visited.ShouldBe(3);
        database.UnknownTypes.ShouldBe(1);
        database.Rewritten.ShouldBe(2);
        database.Completed.ShouldBeTrue();

        (await storedRowsAsync()).Select(x => x.Type)
            .ShouldBe(["person_registered", "retired_event", "person_registered"]);
    }

    [Fact]
    public async Task cancelling_stops_between_pages_and_the_last_reported_cursor_resumes()
    {
        withUpcaster();
        for (var i = 0; i < 5; i++) await appendAsync(theStore, new PersonRegisteredV1($"Person {i}"));

        using var cancellation = new CancellationTokenSource();
        var progress = new List<StoredEventRewriteProgress>();
        var cancelled = false;

        try
        {
            await theStore.Advanced.RewriteStoredEventsAsync(x =>
            {
                x.IncludeType<PersonRegistered>();
                x.BatchSize = 2;
                x.OnProgress = (p, _) =>
                {
                    progress.Add(p);
                    cancellation.Cancel();
                    return Task.CompletedTask;
                };
            }, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }

        cancelled.ShouldBeTrue();
        progress.Single().Visited.ShouldBe(2);
        progress.Single().Completed.ShouldBeFalse();
        (await storedRowsAsync()).Count(x => x.Type == "person_registered").ShouldBe(2);

        var result = await theStore.Advanced.RewriteStoredEventsAsync(x => x
            .IncludeType<PersonRegistered>()
            .ResumeFrom(progress.Single().Cursor));

        result.Databases.Single().Visited.ShouldBe(3);
        (await storedRowsAsync()).ShouldAllBe(x => x.Type == "person_registered");
    }

    [Fact]
    public async Task a_transform_that_returns_null_is_refused()
    {
        withUpcaster();
        await appendAsync(theStore, new PersonMoved("Utrecht"));

        await Should.ThrowAsync<InvalidOperationException>(() =>
            theStore.Advanced.RewriteStoredEventsAsync(x => x.Transform<PersonMoved>(_ => null!)));

        (await storedRowsAsync()).Single().Type.ShouldBe("person_moved");
    }
}
