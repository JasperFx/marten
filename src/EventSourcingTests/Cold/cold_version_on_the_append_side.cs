using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using Marten;
using Marten.Events.Operations;
using Marten.Events.Schema;
using Marten.Exceptions;
using Marten.Internal.Sessions;
using Marten.Storage;
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
        public bool Asks = true;

        /// <summary>Runs once, right after the listener looked: a concurrent writer committing in between.</summary>
        public Func<Task>? Meanwhile;

        public override async Task BeforeSaveChangesAsync(IDocumentSession session, CancellationToken token)
        {
            if (!Asks) return;

            foreach (var stream in session.PendingChanges.Streams())
            {
                Seen[stream.Key!] = await session.Events.ColdVersionOfAsync(stream, token);
            }

            var meanwhile = Meanwhile;
            Meanwhile = null;
            if (meanwhile != null) await meanwhile();
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

    private void configure(bool conjoined = false)
    {
        StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            if (conjoined) opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.UseColdEventStore(_cold);
            opts.Listeners.Add(_listener);
        });
    }

    private async Task<string> cartForTenants(params string[] tenants)
    {
        var key = Guid.NewGuid().ToString();
        foreach (var tenant in tenants)
        {
            await using var session = theStore.LightweightSession(tenant);
            session.Events.StartStream<Cart>(key, new CartOpened(tenant), new ItemAdded("apple"));
            await session.SaveChangesAsync();
        }

        return key;
    }

    [Fact]
    public async Task a_listener_sees_the_cold_version_of_the_tenant_an_append_was_made_for()
    {
        configure(conjoined: true);
        var key = await cartForTenants("alpha", "beta");
        await theStore.MoveToColdAsync(_cold, "beta", key, 2);

        await using var session = theStore.LightweightSession("alpha");
        session.ForTenant("beta").Events.Append(key, new ItemAdded("pear"));
        await session.SaveChangesAsync();

        _listener.Seen[key].ShouldBe(2);
    }

    [Fact]
    public async Task a_fetch_for_another_tenant_is_verified_when_the_session_commits()
    {
        configure(conjoined: true);
        var key = await cartForTenants("alpha", "beta");

        _listener.Asks = false;
        await using var writer = theStore.LightweightSession("alpha");
        var stream = await writer.ForTenant("beta").Events.FetchForWriting<Cart>(key);
        stream.AppendOne(new ItemAdded("pear"));

        await theStore.MoveToColdAsync(_cold, "beta", key, 2);

        await Should.ThrowAsync<ColdStreamChangedException>(() => writer.SaveChangesAsync());

        await using var query = theStore.LightweightSession("beta");
        (await query.Events.FetchStreamStateAsync(key))!.Version.ShouldBe(2);
    }

    [Fact]
    public async Task the_cold_version_of_a_stream_this_session_does_not_append_to()
    {
        configure();
        var key = await coldCart();

        _listener.Asks = false;
        await using var other = theStore.LightweightSession();
        other.Events.Append(key, new ItemAdded("pear"));

        await using var session = theStore.LightweightSession();
        (await session.Events.ColdVersionOfAsync(other.PendingChanges.Streams().Single())).ShouldBe(2);
    }

    [Fact]
    public async Task a_retried_commit_verifies_the_cold_versions_once()
    {
        configure();
        var key = Guid.NewGuid().ToString();
        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream<Cart>(key, new CartOpened("anne"), new ItemAdded("apple"));
            await session.SaveChangesAsync();
        }

        _listener.Asks = false;
        await using var writer = theStore.LightweightSession();
        var stream = await writer.Events.FetchForWriting<Cart>(key);
        stream.AppendOne(new ItemAdded("pear"));
        await theStore.MoveToColdAsync(_cold, StorageConstants.DefaultTenantId, key, 2);

        await Should.ThrowAsync<ColdStreamChangedException>(() => writer.SaveChangesAsync());

        // The retry fails on its own re-queued append; what matters is what it queued
        await Should.ThrowAsync<Exception>(() => writer.SaveChangesAsync());

        ((DocumentSessionBase)writer).WorkTracker.AllOperations.OfType<AssertColdVersionsOperation>()
            .ShouldHaveSingleItem();
    }

    [Fact]
    public async Task a_listener_sees_the_cold_version_after_fetch_for_writing()
    {
        configure();
        var key = await coldCart();

        await using var session = theStore.LightweightSession();
        var stream = await session.Events.FetchForWriting<Cart>(key);
        stream.AppendOne(new ItemAdded("pear"));
        (await session.Events.ColdVersionOfAsync(session.PendingChanges.Streams().Single())).ShouldBe(2);
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
    public async Task a_stream_that_went_cold_after_fetch_for_writing_fails_the_commit()
    {
        configure();
        var key = Guid.NewGuid().ToString();
        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream<Cart>(key, new CartOpened("anne"), new ItemAdded("apple"));
            await session.SaveChangesAsync();
        }

        _listener.Asks = false;
        await using var writer = theStore.LightweightSession();
        var stream = await writer.Events.FetchForWriting<Cart>(key);
        stream.AppendOne(new ItemAdded("pear"));

        // Another process moves the stream to cold storage before this one commits
        await theStore.MoveToColdAsync(_cold, StorageConstants.DefaultTenantId, key, 2);

        var e = await Should.ThrowAsync<ColdStreamChangedException>(() => writer.SaveChangesAsync());
        e.Message.ShouldContain(key);

        await using var query = theStore.LightweightSession();
        (await query.Events.FetchStreamStateAsync(key))!.Version.ShouldBe(2);
        (await query.Events.FetchLatest<Cart>(key))!.Items.ShouldBe(["apple"]);
    }

    [Fact]
    public async Task a_stream_that_went_cold_after_a_listener_looked_fails_the_commit()
    {
        configure();
        var key = Guid.NewGuid().ToString();
        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream<Cart>(key, new CartOpened("anne"), new ItemAdded("apple"));
            await session.SaveChangesAsync();
        }

        _listener.Meanwhile = () => theStore.MoveToColdAsync(_cold, StorageConstants.DefaultTenantId, key, 2);

        await using var writer = theStore.LightweightSession();
        writer.Events.Append(key, new ItemAdded("pear"));
        await Should.ThrowAsync<ColdStreamChangedException>(() => writer.SaveChangesAsync());

        _listener.Seen[key].ShouldBeNull();
    }

    [Fact]
    public async Task nothing_is_checked_when_nobody_looked()
    {
        configure();
        var key = Guid.NewGuid().ToString();
        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream<Cart>(key, new CartOpened("anne"), new ItemAdded("apple"));
            await session.SaveChangesAsync();
        }

        _listener.Asks = false;
        await using var writer = theStore.LightweightSession();
        writer.Events.Append(key, new ItemAdded("pear"));
        await theStore.MoveToColdAsync(_cold, StorageConstants.DefaultTenantId, key, 2);
        await writer.SaveChangesAsync();

        (await theStore.LightweightSession().Events.FetchLatest<Cart>(key))!.Items.ShouldBe(["apple", "pear"]);
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
