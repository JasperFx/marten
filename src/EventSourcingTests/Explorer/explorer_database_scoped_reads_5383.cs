#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Descriptors;
using JasperFx.Events;
using Marten;
using Marten.Storage;
using Marten.Testing.Harness;
using Npgsql;
using Weasel.Postgresql;
using Weasel.Postgresql.Migrations;
using Shouldly;
using Weasel.Core;
using Xunit;

namespace EventSourcingTests.Explorer;

/// <summary>
/// #5383 parts 1 and 3 — the explorer reads take an <see cref="IEventDatabase"/>, and a store-global
/// read on a multi-database store stops answering from one database as though it were everything.
/// </summary>
/// <remarks>
/// <para>
/// The shape jasperfx#810 was filed for: a tool enumerates <c>AllDatabases()</c>, reads each database,
/// and attributes every row to the database it came from. Before this, the tenant-less overloads
/// answered from whichever database the store's default session resolved — <b>indistinguishable from a
/// complete answer</b>, which is the only option that is actually wrong.
/// </para>
/// <para>
/// Listings and single-answer reads diverge deliberately, and both directions are pinned here: a
/// listing has a well-defined union so it fans out and merges, while a stream id is unique WITHIN a
/// database rather than across them, so a single-stream read refuses and names the overload to use.
/// </para>
/// </remarks>
[Collection("multi-tenancy")]
public class explorer_database_scoped_reads_5383 : IAsyncLifetime
{
    private static readonly string[] TenantDatabases = ["explorer_5383_north", "explorer_5383_south"];

    private readonly Dictionary<string, string> _connectionStrings = new();
    private IDocumentStore _store = null!;

    public async ValueTask InitializeAsync()
    {
        // Two GENUINELY separate physical databases. Pointing both tenants at one database would make
        // every assertion here pass for the wrong reason — the rows would be co-located and the reads
        // would agree by accident.
        await using (var conn = new NpgsqlConnection(ConnectionSource.ConnectionString))
        {
            await conn.OpenAsync();
            foreach (var db in TenantDatabases)
            {
                if (!await conn.DatabaseExists(db))
                {
                    await new DatabaseSpecification().BuildDatabase(conn, db);
                }

                var builder = new NpgsqlConnectionStringBuilder(ConnectionSource.ConnectionString) { Database = db };
                _connectionStrings[db] = builder.ConnectionString;
            }
        }

        _store = DocumentStore.For(opts =>
        {
            opts.MultiTenantedDatabases(x =>
            {
                x.AddSingleTenantDatabase(_connectionStrings[TenantDatabases[0]], "north");
                x.AddSingleTenantDatabase(_connectionStrings[TenantDatabases[1]], "south");
            });
            opts.DatabaseSchemaName = "explorer_5383";
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.AutoCreateSchemaObjects = AutoCreate.All;
            opts.Events.AddEventType<ExplorerScopedEvent>();
        });

        await _store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        // Nothing clears these databases between runs and every test appends the same stream keys.
        foreach (var connectionString in _connectionStrings.Values)
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "truncate table explorer_5383.mt_events, explorer_5383.mt_streams";
            await cmd.ExecuteNonQueryAsync();
        }
    }

    public ValueTask DisposeAsync()
    {
        _store.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task appendAsync(string tenantId, string streamKey)
    {
        await using var session = _store.LightweightSession(tenantId);
        session.Events.Append(streamKey, new ExplorerScopedEvent { Name = streamKey });
        await session.SaveChangesAsync();
    }

    private async Task<IReadOnlyList<IEventDatabase>> databasesAsync() =>
        await ((IEventStore)_store).AllDatabases();

    [Fact]
    public async Task each_database_answers_for_itself_and_only_itself()
    {
        await appendAsync("north", "north-1");
        await appendAsync("south", "south-1");

        var store = (IEventStore)_store;
        var byDatabase = new Dictionary<string, List<string>>();
        foreach (var database in await databasesAsync())
        {
            var streams = await store.GetRecentStreamsAsync(database, 50, null, CancellationToken.None);
            byDatabase[database.Identifier] = streams.Select(x => x.StreamId).ToList();
        }

        // Two databases, and each one's rows are its own — which is the attribution the caller could
        // not make before, because it only ever got one unlabelled answer.
        byDatabase.Count.ShouldBe(2);
        byDatabase.Values.SelectMany(x => x).OrderBy(x => x).ShouldBe(["north-1", "south-1"]);
        byDatabase.Single(kv => kv.Value.Contains("north-1")).Value.ShouldNotContain("south-1");
    }

    [Fact]
    public async Task a_database_scoped_stream_read_finds_only_that_databases_stream()
    {
        await appendAsync("north", "north-1");
        await appendAsync("south", "south-1");

        var store = (IEventStore)_store;
        var databases = await databasesAsync();

        var found = new List<string>();
        foreach (var database in databases)
        {
            var events = new List<EventRecord>();
            await foreach (var e in store.ReadStreamAsync(database, "north-1", null, CancellationToken.None))
            {
                events.Add(e);
            }

            if (events.Count > 0) found.Add(database.Identifier);
        }

        // Exactly one database has it. The read that could not be scoped before would have answered
        // from whichever database the default session resolved — right half the time, silent always.
        found.Count.ShouldBe(1);
    }

    [Fact]
    public async Task a_store_global_listing_fans_out_across_every_database()
    {
        await appendAsync("north", "north-1");
        await appendAsync("south", "south-1");

        // A listing HAS a union, so it merges rather than refusing — and the union is what makes it a
        // complete answer rather than one database's rows wearing the whole store's name.
        var streams = await ((IEventStore)_store).GetRecentStreamsAsync(50, CancellationToken.None);

        streams.Select(x => x.StreamId).OrderBy(x => x).ShouldBe(["north-1", "south-1"]);
    }

    [Fact]
    public async Task a_store_global_single_stream_read_refuses_and_names_the_overload()
    {
        await appendAsync("north", "north-1");

        var store = (IEventStore)_store;

        // A stream id is unique WITHIN a database, so there is no merge that yields the one answer
        // these signatures return. Refusing is the honest option; answering from one database is not.
        var refused = await Should.ThrowAsync<NotSupportedException>(async () =>
        {
            await foreach (var _ in store.ReadStreamAsync("north-1", CancellationToken.None)) { }
        });
        refused.Message.ShouldContain(nameof(IEventStore.ReadStreamAsync));
        refused.Message.ShouldContain("AllDatabases");

        var metadataRefusal = await Should.ThrowAsync<NotSupportedException>(
            () => store.GetStreamMetadataAsync("north-1", CancellationToken.None));
        metadataRefusal.Message.ShouldContain("AllDatabases");
    }

    [Fact]
    public async Task projection_statuses_read_per_database_report_that_databases_own_progression()
    {
        await appendAsync("north", "north-1");

        var store = (IEventStore)_store;
        foreach (var database in await databasesAsync())
        {
            // The point against #5382's registry answer, which reports every sequence as 0 because it
            // reads nothing: this overload reads, so it can differ per database.
            var statuses = await store.GetProjectionStatusesAsync(database, null, CancellationToken.None);
            statuses.ShouldNotBeNull();
        }
    }
}

public class ExplorerScopedEvent
{
    public string Name { get; set; } = "";
}
