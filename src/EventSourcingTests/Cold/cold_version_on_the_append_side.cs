using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using Marten;
using Marten.Events.Schema;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace EventSourcingTests.Cold;

public class cold_version_on_the_append_side: OneOffConfigurationsContext
{
    private readonly InMemoryColdEventStore _cold = new();
    private readonly ColdVersionListener _listener = new();

    public class ColdVersionListener: DocumentSessionListenerBase
    {
        public readonly Dictionary<string, long?> Seen = new();

        public override Task BeforeSaveChangesAsync(IDocumentSession session, CancellationToken token)
        {
            foreach (var stream in session.PendingChanges.Streams())
            {
                Seen[stream.Key!] = session.Events.ColdVersionOf(stream);
            }

            return Task.CompletedTask;
        }
    }

    private async Task<string> coldCart()
    {
        var key = Guid.NewGuid().ToString();
        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream<Cart>(key, new CartOpened("anne"), new ItemAdded("apple"));
            await session.SaveChangesAsync();
        }

        await theStore.MoveToColdAsync(_cold, StorageConstants.DefaultTenantId, key, 2);
        return key;
    }

    private void configure()
    {
        StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Events.UseColdEventStore(_cold);
            opts.Listeners.Add(_listener);
        });
    }

    [Fact]
    public async Task a_listener_sees_the_cold_version_after_fetch_for_writing()
    {
        configure();
        var key = await coldCart();

        await using var session = theStore.LightweightSession();
        var stream = await session.Events.FetchForWriting<Cart>(key);
        stream.AppendOne(new ItemAdded("pear"));
        session.Events.ColdVersionOf(session.PendingChanges.Streams().Single()).ShouldBe(2);
        await session.SaveChangesAsync();

        _listener.Seen[key].ShouldBe(2);
    }

    [Fact]
    public async Task a_listener_sees_the_cold_version_of_a_plain_append()
    {
        configure();
        var cold = await coldCart();
        var warm = Guid.NewGuid().ToString();
        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream<Cart>(warm, new CartOpened("anne"));
            await session.SaveChangesAsync();
        }

        var started = Guid.NewGuid().ToString();

        await using (var session = theStore.LightweightSession())
        {
            session.Events.Append(cold, new ItemAdded("pear"));
            session.Events.Append(warm, new ItemAdded("plum"));
            session.Events.StartStream<Cart>(started, new CartOpened("anne"));
            await session.SaveChangesAsync();
        }

        _listener.Seen[cold].ShouldBe(2);
        _listener.Seen[warm].ShouldBeNull();
        _listener.Seen[started].ShouldBeNull();
    }

    [Fact]
    public async Task the_cold_version_column_only_exists_with_a_cold_store()
    {
        StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Events.AddEventType<CartOpened>();
        });
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        (await streamsColumns()).ShouldContain("version");
        (await streamsColumns()).ShouldNotContain(StreamsTable.ColdVersionColumn);

        // Same schema, now with a cold store: a normal migration adds the column
        StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Events.AddEventType<CartOpened>();
            opts.Events.UseColdEventStore(_cold);
        }, cleanAll: false);
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        (await streamsColumns()).ShouldContain(StreamsTable.ColdVersionColumn);
    }

    private async Task<IReadOnlyList<string>> streamsColumns()
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        var columns = new List<string>();
        await using var reader = await conn.CreateCommand(
                $"select column_name from information_schema.columns where table_schema = '{SchemaName}' and table_name = 'mt_streams'")
            .ExecuteReaderAsync();
        while (await reader.ReadAsync()) columns.Add(reader.GetString(0));
        return columns;
    }
}
