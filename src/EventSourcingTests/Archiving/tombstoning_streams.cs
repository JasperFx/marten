using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using JasperFx.Events.Projections;
using JasperFx.Events.Tags;
using Marten;
using Marten.Storage;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql;
using Xunit;

namespace EventSourcingTests.Archiving;

/// <summary>
/// marten#5673: the Marten-specific half of <c>IEventStoreOperations.TombstoneStream</c>. The shared
/// StreamTombstoningCompliance suite (enrolled as <c>stream_tombstoning_compliance</c>) pins the
/// contract; these facts pin the Postgres storage shapes it does not reach: archived-stream
/// partitioning, per-tenant event partitioning, strict stream identity enforcement, the identity
/// mismatch guard, and the projected documents a hard delete deliberately leaves alone.
/// </summary>
public class tombstoning_streams: OneOffConfigurationsContext
{
    public record LedgerOpened(string Name);
    public record LedgerPosted(decimal Amount);
    public record LedgerId(Guid Value);

    public class Ledger
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal Balance { get; set; }

        public static Ledger Create(LedgerOpened e) => new() { Name = e.Name };
        public void Apply(LedgerPosted e) => Balance += e.Amount;
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task tombstoning_reaches_both_archive_partitions(bool stringIdentity, bool conjoined)
    {
        StoreOptions(opts =>
        {
            opts.Events.UseArchivedStreamPartitioning = true;
            opts.Events.StreamIdentity = stringIdentity ? StreamIdentity.AsString : StreamIdentity.AsGuid;
            opts.Events.TenancyStyle = conjoined ? TenancyStyle.Conjoined : TenancyStyle.Single;
            opts.Events.AddEventType<LedgerOpened>();
            opts.Events.AddEventType<LedgerPosted>();
        });

        var tenant = conjoined ? "one" : StorageConstants.DefaultTenantId;
        object archived = stringIdentity ? Guid.NewGuid().ToString() : Guid.NewGuid();
        object live = stringIdentity ? Guid.NewGuid().ToString() : Guid.NewGuid();

        await using (var session = theStore.LightweightSession(tenant))
        {
            startStream(session, archived);
            startStream(session, live);
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession(tenant))
        {
            if (archived is Guid g) session.Events.ArchiveStream(g);
            else session.Events.ArchiveStream((string)archived);
            await session.SaveChangesAsync();
        }

        (await countRows("mt_events", archived, tenant, isArchived: true)).ShouldBe(2);

        await using (var session = theStore.LightweightSession(tenant))
        {
            tombstone(session, archived);
            tombstone(session, live);
            await session.SaveChangesAsync();
        }

        foreach (var id in new[] { archived, live })
        {
            foreach (var isArchived in new[] { true, false })
            {
                (await countRows("mt_events", id, tenant, isArchived)).ShouldBe(0);
                (await countRows("mt_streams", id, tenant, isArchived)).ShouldBe(0);
            }
        }
    }

    [Fact]
    public async Task tombstoning_deletes_tag_rows_in_a_partitioned_tag_table()
    {
        StoreOptions(opts =>
        {
            opts.Events.UseArchivedStreamPartitioning = true;
            opts.Events.AddEventType<LedgerOpened>();
            opts.Events.AddEventType<LedgerPosted>();

            // The trim- and AOT-safe registration (jasperfx#1012), exercised here so it has a caller.
            opts.Events.RegisterTagType<LedgerId, Guid>(id => id.Value, "ledger");
        });

        var doomed = Guid.NewGuid();
        var kept = Guid.NewGuid();
        var doomedTag = new LedgerId(Guid.NewGuid());
        var keptTag = new LedgerId(Guid.NewGuid());

        await using (var session = theStore.LightweightSession())
        {
            var opened = session.Events.BuildEvent(new LedgerOpened("doomed"));
            opened.WithTag(doomedTag);
            var posted = session.Events.BuildEvent(new LedgerPosted(10));
            posted.WithTag(doomedTag);
            session.Events.StartStream(doomed, opened, posted);

            var neighbour = session.Events.BuildEvent(new LedgerOpened("kept"));
            neighbour.WithTag(keptTag);
            session.Events.StartStream(kept, neighbour);

            await session.SaveChangesAsync();
        }

        // Not archived first: archiving a TAGGED stream under UseArchivedStreamPartitioning fails today,
        // because mt_archive_stream moves the events to the archived partition without moving the tag
        // rows that reference them (FK violation). That is an archiving bug, independent of this delete.

        await using (var session = theStore.LightweightSession())
        {
            session.Events.TombstoneStream(doomed);
            await session.SaveChangesAsync();
        }

        (await countTagRows(doomedTag.Value)).ShouldBe(0);
        (await countTagRows(keptTag.Value)).ShouldBe(1);
    }

    [Fact]
    public async Task tombstoning_under_tenant_partitioned_events_deletes_only_the_sessions_tenant()
    {
        StoreOptions(opts =>
        {
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;
            opts.Events.UseTenantPartitionedEvents = true;
            opts.Policies.AllDocumentsAreMultiTenanted();
            opts.Policies.PartitionMultiTenantedDocumentsUsingMartenManagement(SchemaName + "_tenants");
            opts.Events.AddEventType<LedgerOpened>();
            opts.Events.AddEventType<LedgerPosted>();
        });

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        await theStore.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, "acme", "globex");

        var streamId = Guid.NewGuid();
        foreach (var tenant in new[] { "acme", "globex" })
        {
            await using var session = theStore.LightweightSession(tenant);
            session.Events.StartStream(streamId, new LedgerOpened(tenant), new LedgerPosted(1));
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession("acme"))
        {
            session.Events.TombstoneStream(streamId);
            await session.SaveChangesAsync();
        }

        await using (var acme = theStore.LightweightSession("acme"))
        {
            (await acme.Events.FetchStreamAsync(streamId, token: TestContext.Current.CancellationToken)).ShouldBeEmpty();
            (await acme.Events.FetchStreamStateAsync(streamId, TestContext.Current.CancellationToken)).ShouldBeNull();
        }

        await using var globex = theStore.LightweightSession("globex");
        (await globex.Events.FetchStreamAsync(streamId, token: TestContext.Current.CancellationToken)).Count.ShouldBe(2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task a_tombstoned_identity_is_free_again_under_strict_identity_enforcement(bool usePartitioning)
    {
        StoreOptions(opts =>
        {
            opts.Events.UseArchivedStreamPartitioning = usePartitioning;
            opts.Events.EnableStrictStreamIdentityEnforcement = true;
            opts.Events.AddEventType<LedgerOpened>();
        });

        var streamId = Guid.NewGuid();

        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream(streamId, new LedgerOpened("first"));
            await session.SaveChangesAsync();

            session.Events.TombstoneStream(streamId);
            await session.SaveChangesAsync();
        }

        // Archiving leaves the identity claimed (see strict_stream_identity_enforcement); a hard delete
        // must not, or it would be a soft delete with extra steps.
        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream(streamId, new LedgerOpened("second"));
            await session.SaveChangesAsync();
        }

        await using var query = theStore.QuerySession();
        var state = await query.Events.FetchStreamStateAsync(streamId, TestContext.Current.CancellationToken);
        state.ShouldNotBeNull();
        state.Version.ShouldBe(1);
    }

    [Fact]
    public async Task tombstoning_leaves_projected_documents_alone()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Snapshot<Ledger>(SnapshotLifecycle.Inline);
        });

        var streamId = Guid.NewGuid();
        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream<Ledger>(streamId, new LedgerOpened("books"), new LedgerPosted(25));
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            session.Events.TombstoneStream(streamId);
            await session.SaveChangesAsync();
        }

        await using var query = theStore.QuerySession();
        (await query.Events.FetchStreamStateAsync(streamId, TestContext.Current.CancellationToken)).ShouldBeNull();

        var ledger = await query.LoadAsync<Ledger>(streamId, TestContext.Current.CancellationToken);
        ledger.ShouldNotBeNull();
        ledger.Balance.ShouldBe(25);
    }

    [Fact]
    public void tombstoning_with_the_wrong_identity_kind_is_refused()
    {
        StoreOptions(opts => opts.Events.StreamIdentity = StreamIdentity.AsGuid);

        using var session = theStore.LightweightSession();
        Should.Throw<InvalidOperationException>(() => session.Events.TombstoneStream("not-a-guid-stream"));
    }

    [Fact]
    public async Task sample_usage()
    {
        StoreOptions(opts => opts.Events.AddEventType<LedgerOpened>());

        var streamId = Guid.NewGuid();
        await using (var start = theStore.LightweightSession())
        {
            start.Events.StartStream(streamId, new LedgerOpened("to purge"));
            await start.SaveChangesAsync();
        }

        #region sample_tombstone_stream

        await using var session = theStore.LightweightSession();

        // Queue a hard delete of the stream, its events and their tag rows.
        // Nothing touches the database until SaveChangesAsync.
        session.Events.TombstoneStream(streamId);

        // The string-keyed overload is used when StreamIdentity is AsString:
        // session.Events.TombstoneStream("customer-123");

        await session.SaveChangesAsync();

        #endregion

        (await session.Events.FetchStreamStateAsync(streamId)).ShouldBeNull();
    }

    private static void startStream(IDocumentSession session, object id)
    {
        if (id is Guid guid) session.Events.StartStream(guid, new LedgerOpened("x"), new LedgerPosted(1));
        else session.Events.StartStream((string)id, new LedgerOpened("x"), new LedgerPosted(1));
    }

    private static void tombstone(IDocumentSession session, object id)
    {
        if (id is Guid guid) session.Events.TombstoneStream(guid);
        else session.Events.TombstoneStream((string)id);
    }

    private async Task<long> countRows(string table, object id, string tenant, bool isArchived)
    {
        var idColumn = table == "mt_events" ? "stream_id" : "id";
        await using var connection = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand(
                $"select count(*) from {SchemaName}.{table} where {idColumn} = :id and tenant_id = :tenant and is_archived = :archived")
            .With("id", id).With("tenant", tenant).With("archived", isArchived);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private async Task<long> countTagRows(Guid value)
    {
        await using var connection = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection
            .CreateCommand($"select count(*) from {SchemaName}.mt_event_tag_ledger where value = :value")
            .With("value", value);
        return (long)(await command.ExecuteScalarAsync())!;
    }
}
