using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Core;
using JasperFx.Descriptors;
using JasperFx.Events;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using JasperFx.Events.Tags;
using Marten;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Events.Schema;
using Marten.Schema;
using Marten.Storage;
using Marten.Testing.Harness;
using Microsoft.Extensions.Logging;
using Npgsql;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Parking;

#region Fixtures

public record LedgerOpened(string Number);

public record LedgerPosted;

/// <summary>Throws in the projections below while its stream (and tenant) is in <see cref="LedgerPoison" />.</summary>
public record LedgerBroken;

public static class LedgerPoison
{
    private static readonly ConcurrentDictionary<string, bool> _poisoned = new();

    public static string KeyFor(string tenantId, object stream) => $"{tenantId}/{stream}";

    public static void On(string tenantId, object stream) => _poisoned[KeyFor(tenantId, stream)] = true;

    public static void Off(string tenantId, object stream) => _poisoned.TryRemove(KeyFor(tenantId, stream), out _);

    public static void ThrowIfPoisoned(string tenantId, object identity)
    {
        if (_poisoned.ContainsKey(KeyFor(tenantId, identity)))
        {
            throw new InvalidOperationException($"Ledger {identity} in {tenantId} is poisoned");
        }
    }
}

public record LedgerNumber(string Value);

public interface IParkingLedger
{
    int Postings { get; }
}

public class GuidLedger: IParkingLedger
{
    public Guid Id { get; set; }
    public int Postings { get; set; }

    public void Apply(LedgerOpened e)
    {
    }

    public void Apply(LedgerPosted e) => Postings++;

    public void Apply(IEvent<LedgerBroken> e)
    {
        LedgerPoison.ThrowIfPoisoned(e.TenantId, e.StreamId);
        Postings++;
    }
}

public class StringLedger: IParkingLedger
{
    public string Id { get; set; } = string.Empty;
    public int Postings { get; set; }

    public void Apply(LedgerOpened e)
    {
    }

    public void Apply(LedgerPosted e) => Postings++;

    public void Apply(IEvent<LedgerBroken> e)
    {
        LedgerPoison.ThrowIfPoisoned(e.TenantId, e.StreamKey!);
        Postings++;
    }
}

public class NaturalKeyLedger: IParkingLedger
{
    public Guid Id { get; set; }

    [NaturalKey]
    public LedgerNumber Number { get; set; } = null!;

    public int Postings { get; set; }

    [NaturalKeySource]
    public void Apply(LedgerOpened e) => Number = new LedgerNumber(e.Number);

    public void Apply(LedgerPosted e) => Postings++;

    public void Apply(IEvent<LedgerBroken> e)
    {
        LedgerPoison.ThrowIfPoisoned(e.TenantId, e.StreamId);
        Postings++;
    }
}

/// <summary>The totals of one account, fed by postings that name it.</summary>
public class AccountTotal
{
    public Guid Id { get; set; }
    public int Postings { get; set; }
}

public record AccountPosted(Guid Account);

public record AccountBroken(Guid Account);

public readonly record struct AccountTag(Guid Value);

/// <summary>Class B: a plain delegate, made findable by the slice index.</summary>
public class IndexedAccountProjection: MultiStreamProjection<AccountTotal, Guid>
{
    public IndexedAccountProjection()
    {
        Name = "IndexedAccount";
        Identity<AccountPosted>(x => x.Account);
        Identity<AccountBroken>(x => x.Account);
        Options.UseSliceIndex();
    }

    public void Apply(AccountPosted e, AccountTotal total) => total.Postings++;

    public void Apply(IEvent<AccountBroken> e, AccountTotal total)
    {
        LedgerPoison.ThrowIfPoisoned(e.TenantId, e.Data.Account);
        total.Postings++;
    }
}

/// <summary>Class A by tag.</summary>
public class TaggedAccountProjection: MultiStreamProjection<AccountTotal, Guid>
{
    public TaggedAccountProjection()
    {
        Name = "TaggedAccount";
        IdentityFromTag<AccountTag>();
        IncludeType<AccountPosted>();
        IncludeType<AccountBroken>();
    }

    public void Apply(AccountPosted e, AccountTotal total) => total.Postings++;

    public void Apply(IEvent<AccountBroken> e, AccountTotal total)
    {
        LedgerPoison.ThrowIfPoisoned(e.TenantId, e.Data.Account);
        total.Postings++;
    }
}

/// <summary>Class C: a plain delegate with no slice index.</summary>
public class ScanOnlyAccountProjection: MultiStreamProjection<AccountTotal, Guid>
{
    public ScanOnlyAccountProjection()
    {
        Name = "ScanOnlyAccount";
        Identity<AccountPosted>(x => x.Account);
    }

    public void Apply(AccountPosted e, AccountTotal total) => total.Postings++;
}

/// <summary>Raises an event from its side effects, in a store with a DCB tag type registered.</summary>
public class AuditingLedgerProjection: SingleStreamProjection<GuidLedger, Guid>
{
    public AuditingLedgerProjection()
    {
        Name = "AuditingLedger";
    }

    public void Apply(LedgerOpened e, GuidLedger ledger)
    {
    }

    public void Apply(LedgerPosted e, GuidLedger ledger) => ledger.Postings++;

    public override ValueTask RaiseSideEffects(IDocumentOperations operations, IEventSlice<GuidLedger> slice)
    {
        if (slice.Snapshot != null)
        {
            slice.AppendEvent(Guid.NewGuid(), new AccountPosted(slice.Snapshot.Id));
        }

        return new ValueTask();
    }
}

#endregion

/// <summary>
///     marten#6003: what Marten adds around JasperFx 3.10's parking, beyond the compliance suites: the fetch APIs over
///     a parked identity for Guid, string and natural-key aggregates, conjoined multi-stream parking, the tables and
///     their cleanup, tag hydration in both DCB storage modes, and the startup log.
/// </summary>
public class parking_failing_identities: OneOffConfigurationsContext
{
    private static readonly TimeSpan Generous = 60.Seconds();

    private readonly List<(string Tenant, object Stream)> _poisoned = new();

    public override async ValueTask DisposeAsync()
    {
        foreach (var (tenant, stream) in _poisoned) LedgerPoison.Off(tenant, stream);
        await base.DisposeAsync();
    }

    private void poison(object stream, string tenantId = StorageConstants.DefaultTenantId)
    {
        LedgerPoison.On(tenantId, stream);
        _poisoned.Add((tenantId, stream));
    }

    private static void unpoison(object stream, string tenantId = StorageConstants.DefaultTenantId)
        => LedgerPoison.Off(tenantId, stream);

    private async Task<IReadOnlyList<ParkedIdentity>> parkedAsync(string? projectionName = null)
        => await ((IEventDatabase)theStore.Storage.Database).FetchParkedIdentitiesAsync(
            new ParkedIdentityQuery { ProjectionName = projectionName }, TestContext.Current.CancellationToken);

    private Task waitAsync() => theStore.WaitForNonStaleProjectionDataAsync(Generous);

    private async Task<long> countAsync(string table, string? where = null)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = conn.CreateCommand();
        command.CommandText = $"select count(*) from {SchemaName}.{table}" + (where == null ? "" : " where " + where);
        return (long)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    // ---- FetchForWriting, FetchLatest and FetchManyForWriting around a park (Jeremy's matrix) ----

    public static TheoryData<string> LedgerKinds => new() { "guid", "string", "natural-key" };

    private void configureLedger(string kind)
    {
        StoreOptions(opts =>
        {
            opts.Projections.Errors.ApplyErrors = ProjectionErrorAction.Park;
            switch (kind)
            {
                case "guid":
                    opts.Projections.Snapshot<GuidLedger>(SnapshotLifecycle.Async);
                    break;
                case "string":
                    opts.Events.StreamIdentity = StreamIdentity.AsString;
                    opts.Projections.Snapshot<StringLedger>(SnapshotLifecycle.Async);
                    break;
                default:
                    opts.Projections.Snapshot<NaturalKeyLedger>(SnapshotLifecycle.Async);
                    break;
            }
        });
    }

    private async Task<object> aLedgerAsync(string kind, string number, params object[] events)
    {
        await using var session = theStore.LightweightSession();
        var all = new object[] { new LedgerOpened(number) }.Concat(events).ToArray();

        object stream;
        switch (kind)
        {
            case "guid":
                stream = session.Events.StartStream<GuidLedger>(all).Id;
                break;
            case "string":
                stream = $"ledger-{Guid.NewGuid():N}";
                session.Events.StartStream<StringLedger>((string)stream, all);
                break;
            default:
                stream = session.Events.StartStream<NaturalKeyLedger>(all).Id;
                break;
        }

        await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        return stream;
    }

    private async Task appendAsync(object stream, params object[] events)
    {
        await using var session = theStore.LightweightSession();
        if (stream is Guid id) session.Events.Append(id, events);
        else session.Events.Append((string)stream, events);
        await session.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<(int? Writing, int? Latest, int? Many)> fetchAsync(string kind, object stream, string number)
    {
        var token = TestContext.Current.CancellationToken;
        await using var session = theStore.LightweightSession();

        switch (kind)
        {
            case "guid":
            {
                var writing = await session.Events.FetchForWriting<GuidLedger>((Guid)stream, token);
                var latest = await session.Events.FetchLatest<GuidLedger>((Guid)stream, token);
                var many = await session.Events.FetchManyForWriting<GuidLedger>([(Guid)stream], token);
                return (writing.Aggregate?.Postings, latest?.Postings, many.Single().Aggregate?.Postings);
            }
            case "string":
            {
                var writing = await session.Events.FetchForWriting<StringLedger>((string)stream, token);
                var latest = await session.Events.FetchLatest<StringLedger>((string)stream, token);
                var many = await session.Events.FetchManyForWriting<StringLedger>([(string)stream], token);
                return (writing.Aggregate?.Postings, latest?.Postings, many.Single().Aggregate?.Postings);
            }
            default:
            {
                // By the natural key, and FetchManyForWriting by the stream it resolves to
                var key = new LedgerNumber(number);
                var writing = await session.Events.FetchForWriting<NaturalKeyLedger, LedgerNumber>(key, token);
                var latest = await session.Events.FetchLatest<NaturalKeyLedger, LedgerNumber>(key, token);
                var many = await session.Events.FetchManyForWriting<NaturalKeyLedger>([(Guid)stream], token);
                return (writing.Aggregate?.Postings, latest?.Postings, many.Single().Aggregate?.Postings);
            }
        }
    }

    private async Task<int?> storedPostingsAsync(string kind, object stream)
    {
        await using var query = theStore.QuerySession();
        IParkingLedger? ledger = kind switch
        {
            "guid" => await query.LoadAsync<GuidLedger>((Guid)stream, TestContext.Current.CancellationToken),
            "string" => await query.LoadAsync<StringLedger>((string)stream, TestContext.Current.CancellationToken),
            _ => await query.LoadAsync<NaturalKeyLedger>((Guid)stream, TestContext.Current.CancellationToken)
        };

        return ledger?.Postings;
    }

    private static string projectionNameFor(string kind) => kind switch
    {
        "guid" => nameof(GuidLedger),
        "string" => nameof(StringLedger),
        _ => nameof(NaturalKeyLedger)
    };

    [Theory]
    [MemberData(nameof(LedgerKinds))]
    public async Task the_fetch_apis_see_the_right_aggregate_before_while_and_after_a_park(string kind)
    {
        configureLedger(kind);
        var number = $"L-{Guid.NewGuid():N}";
        var stream = await aLedgerAsync(kind, number, new LedgerPosted(), new LedgerPosted());
        var bystander = await aLedgerAsync(kind, $"L-{Guid.NewGuid():N}", new LedgerPosted());

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();
        await waitAsync();

        // Before the park
        (await fetchAsync(kind, stream, number)).ShouldBe((2, 2, 2));

        poison(stream);
        await appendAsync(stream, new LedgerBroken(), new LedgerPosted());
        await appendAsync(bystander, new LedgerPosted());
        await waitAsync();

        // Parked by its stream, whatever the aggregate's own identity looks like
        var row = (await parkedAsync()).ShouldHaveSingleItem();
        row.ProjectionName.ShouldBe(projectionNameFor(kind));
        row.IdentityText.ShouldBe(stream is Guid id ? id.ToString("D") : (string)stream);
        row.IdentityType.ShouldBe(stream is Guid ? "guid" : "string");
        row.Category.ShouldBe(ShardFailureCategory.ApplyEvent);

        // While parked: the stored document is frozen, the bystander moved on, and a fetch that folds the poisoned
        // event fails the way the projection did
        (await storedPostingsAsync(kind, stream)).ShouldBe(2);
        (await storedPostingsAsync(kind, bystander)).ShouldBe(2);
        await Should.ThrowAsync<Exception>(() => fetchAsync(kind, stream, number));

        // Fixed: every fetch folds past the stored document
        unpoison(stream);
        (await fetchAsync(kind, stream, number)).ShouldBe((4, 4, 4));
        (await storedPostingsAsync(kind, stream)).ShouldBe(2);

        // Replayed: the stored document agrees and nothing is parked
        var result = await daemon.ReplayParkedIdentityAsync(projectionNameFor(kind), null, row.Identity,
            TestContext.Current.CancellationToken);
        result.Outcome.ShouldBe(ParkedReplayOutcome.Replayed);

        await waitAsync();
        (await storedPostingsAsync(kind, stream)).ShouldBe(4);
        (await fetchAsync(kind, stream, number)).ShouldBe((4, 4, 4));
        (await parkedAsync()).ShouldBeEmpty();

        await daemon.StopAllAsync();
    }

    [Fact]
    public async Task a_natural_key_aggregate_parks_by_stream_and_its_key_still_resolves()
    {
        configureLedger("natural-key");
        var number = $"L-{Guid.NewGuid():N}";
        var stream = (Guid)await aLedgerAsync("natural-key", number, new LedgerPosted());

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();
        await waitAsync();

        poison(stream);
        await appendAsync(stream, new LedgerBroken());
        await waitAsync();

        var row = (await parkedAsync(nameof(NaturalKeyLedger))).ShouldHaveSingleItem();
        row.IdentityText.ShouldBe(stream.ToString("D"));

        // Discarding drops the event that still fails and unparks the stream
        var result = await daemon.DiscardParkedIdentityAsync(nameof(NaturalKeyLedger), null, stream,
            TestContext.Current.CancellationToken);
        result.Outcome.ShouldBe(ParkedReplayOutcome.Discarded);

        await appendAsync(stream, new LedgerPosted());
        await waitAsync();

        (await parkedAsync()).ShouldBeEmpty();
        (await storedPostingsAsync("natural-key", stream)).ShouldBe(2);

        // The natural key still finds the stream after the discard
        await using var session = theStore.LightweightSession();
        var latest = await session.Events.FetchLatest<NaturalKeyLedger, LedgerNumber>(new LedgerNumber(number),
            TestContext.Current.CancellationToken);
        latest.ShouldNotBeNull().Id.ShouldBe(stream);

        await daemon.StopAllAsync();
    }

    // ---- multi-stream parking under conjoined tenancy ----

    public static TheoryData<string> ConjoinedKinds => new() { "index", "tag" };

    [Theory]
    [MemberData(nameof(ConjoinedKinds))]
    public async Task parking_one_tenants_account_leaves_the_same_account_in_another_tenant_alone(string kind)
    {
        StoreOptions(opts =>
        {
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Schema.For<AccountTotal>().MultiTenanted();
            opts.Projections.Errors.ApplyErrors = ProjectionErrorAction.Park;
            if (kind == "tag")
            {
                opts.Events.RegisterTagType<AccountTag>("account");
                opts.Projections.Add(new TaggedAccountProjection(), ProjectionLifecycle.Async);
            }
            else
            {
                opts.Projections.Add(new IndexedAccountProjection(), ProjectionLifecycle.Async);
            }
        });

        var projectionName = kind == "tag" ? "TaggedAccount" : "IndexedAccount";
        var account = Guid.NewGuid();

        async Task postAsync(string tenantId, object body)
        {
            await using var session = theStore.LightweightSession(tenantId);
            var e = session.Events.BuildEvent(body);
            if (kind == "tag") e.WithTag(new AccountTag(account));
            session.Events.StartStream(Guid.NewGuid(), e);
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        async Task<int?> postingsAsync(string tenantId)
        {
            await using var query = theStore.QuerySession(tenantId);
            return (await query.LoadAsync<AccountTotal>(account, TestContext.Current.CancellationToken))?.Postings;
        }

        await postAsync("red", new AccountPosted(account));
        await postAsync("blue", new AccountPosted(account));

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();
        await waitAsync();

        poison(account, "red");
        await postAsync("red", new AccountBroken(account));
        await postAsync("red", new AccountPosted(account));
        await postAsync("blue", new AccountBroken(account));
        await postAsync("blue", new AccountPosted(account));
        await waitAsync();

        var row = (await parkedAsync(projectionName)).ShouldHaveSingleItem();
        row.TenantId.ShouldBe("red");
        row.IdentityText.ShouldBe(account.ToString("D"));

        (await postingsAsync("red")).ShouldBe(1);
        (await postingsAsync("blue")).ShouldBe(3);

        if (kind == "index")
        {
            // The index rows are the tenant's own: red's held events are indexed too, under red
            (await countAsync(SliceIndexTable.NameFor(projectionName, 1), "tenant_id = 'red'")).ShouldBe(3);
            (await countAsync(SliceIndexTable.NameFor(projectionName, 1), "tenant_id = 'blue'")).ShouldBe(3);
        }

        unpoison(account, "red");
        (await daemon.ReplayParkedIdentityAsync(projectionName, "red", account,
            TestContext.Current.CancellationToken)).Outcome.ShouldBe(ParkedReplayOutcome.Replayed);
        await waitAsync();

        (await parkedAsync(projectionName)).ShouldBeEmpty();
        (await postingsAsync("red")).ShouldBe(3);
        (await postingsAsync("blue")).ShouldBe(3);

        await daemon.StopAllAsync();
    }

    // ---- tag hydration in HStore mode ----

    [Fact]
    public async Task an_async_tag_grouped_projection_groups_by_hstore_tags()
    {
        StoreOptions(opts =>
        {
            opts.Events.DcbStorageMode = DcbStorageMode.HStore;
            opts.Events.RegisterTagType<AccountTag>("account");
            opts.Projections.Errors.ApplyErrors = ProjectionErrorAction.Park;
            opts.Projections.Add(new TaggedAccountProjection(), ProjectionLifecycle.Async);
        });

        var account = Guid.NewGuid();
        for (var i = 0; i < 3; i++)
        {
            await using var session = theStore.LightweightSession();
            var e = session.Events.BuildEvent(new AccountPosted(account));
            e.WithTag(new AccountTag(account));
            session.Events.StartStream(Guid.NewGuid(), e);
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();
        await waitAsync();

        await using var query = theStore.QuerySession();
        (await query.LoadAsync<AccountTotal>(account, TestContext.Current.CancellationToken))
            .ShouldNotBeNull().Postings.ShouldBe(3);

        // ...and one identity's events read back from the tag column, with their tags
        var page = await ((IEventStore<IDocumentOperations, IQuerySession>)theStore).LoadIdentityEventsAsync(
            (IEventDatabase)theStore.Storage.Database, new IdentityEventsRequest { ProjectionName = "TaggedAccount", Identity = account },
            TestContext.Current.CancellationToken);

        page.Source.ShouldBe(IdentityEventSource.Tag);
        page.Exact.ShouldBeTrue();
        page.Events.Count.ShouldBe(3);
        page.Events.ShouldAllBe(x => x.Tags != null && x.Tags.Any(t => t.Value.Equals(new AccountTag(account))));

        await daemon.StopAllAsync();
    }

    // ---- side effects that append events, in a store with a tag type ----

    [Fact]
    public async Task a_side_effect_can_append_events_in_a_store_with_dcb_tag_types()
    {
        StoreOptions(opts =>
        {
            opts.Events.RegisterTagType<AccountTag>("account");
            opts.Projections.Add(new AuditingLedgerProjection(), ProjectionLifecycle.Async);
        });

        Guid stream;
        await using (var session = theStore.LightweightSession())
        {
            stream = session.Events.StartStream<GuidLedger>(new LedgerOpened("A"), new LedgerPosted()).Id;
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();
        await waitAsync();

        await using var query = theStore.QuerySession();
        var raised = await query.Events.QueryAllRawEvents().Where(x => x.EventTypeName == "account_posted")
            .ToListAsync(TestContext.Current.CancellationToken);
        raised.ShouldHaveSingleItem().Data.ShouldBe(new AccountPosted(stream));

        await daemon.StopAllAsync();
    }

    // ---- the tables and their cleanup ----

    [Fact]
    public async Task the_parked_table_is_always_created_and_a_slice_index_only_for_a_projection_that_asks()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Add(new IndexedAccountProjection(), ProjectionLifecycle.Async);
            opts.Projections.Add(new ScanOnlyAccountProjection(), ProjectionLifecycle.Async);
        });

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = conn.CreateCommand();
        command.CommandText =
            "select table_name, column_name, data_type from information_schema.columns where table_schema = :schema and (table_name = :parked or table_name like 'mt\\_slice\\_index\\_%') order by table_name, ordinal_position";
        command.Parameters.AddWithValue("schema", SchemaName);
        command.Parameters.AddWithValue("parked", ParkedIdentitiesTable.Name);

        var columns = new List<(string Table, string Column, string Type)>();
        await using (var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken))
        {
            while (await reader.ReadAsync(TestContext.Current.CancellationToken))
            {
                columns.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
            }
        }

        columns.Select(x => x.Table).Distinct().OrderBy(x => x)
            .ShouldBe([ParkedIdentitiesTable.Name, "mt_slice_index_indexedaccount_v1"]);

        columns.Where(x => x.Table == "mt_slice_index_indexedaccount_v1").Select(x => (x.Column, x.Type))
            .ShouldBe([("tenant_id", "character varying"), ("identity", "uuid"), ("seq_id", "bigint")]);
    }

    private async Task<(IProjectionDaemon Daemon, Guid Parked)> aParkedAccountAsync()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Errors.ApplyErrors = ProjectionErrorAction.Park;
            opts.Projections.Add(new IndexedAccountProjection(), ProjectionLifecycle.Async);
        });

        var parked = Guid.NewGuid();
        var other = Guid.NewGuid();

        async Task postAsync(object body)
        {
            await using var session = theStore.LightweightSession();
            session.Events.StartStream(Guid.NewGuid(), body);
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await postAsync(new AccountPosted(parked));
        await postAsync(new AccountPosted(other));

        var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();
        await waitAsync();

        poison(parked);
        await postAsync(new AccountBroken(parked));
        await postAsync(new AccountPosted(other));
        await waitAsync();

        (await parkedAsync("IndexedAccount")).ShouldHaveSingleItem();
        (await countAsync("mt_slice_index_indexedaccount_v1")).ShouldBe(4);

        await daemon.StopAllAsync();
        return (daemon, parked);
    }

    [Fact]
    public async Task a_rewind_below_the_park_deletes_it_and_the_index_rows_above_the_floor()
    {
        var (daemon, _) = await aParkedAccountAsync();
        using var _d = daemon;

        var row = (await parkedAsync("IndexedAccount")).Single();
        var floor = row.FirstFailingSequence - 1;

        await ((IEventStore<IDocumentOperations, IQuerySession>)theStore).RewindSubscriptionProgressAsync(
            (IEventDatabase)theStore.Storage.Database, "IndexedAccount", TestContext.Current.CancellationToken, floor);

        (await parkedAsync("IndexedAccount")).ShouldBeEmpty();
        (await countAsync("mt_slice_index_indexedaccount_v1", $"seq_id > {floor}")).ShouldBe(0);
        (await countAsync("mt_slice_index_indexedaccount_v1")).ShouldBe(floor);
    }

    [Fact]
    public async Task a_rewind_at_or_above_the_failing_event_keeps_the_park()
    {
        var (daemon, _) = await aParkedAccountAsync();
        using var _d = daemon;

        var row = (await parkedAsync("IndexedAccount")).Single();

        await ((IEventStore<IDocumentOperations, IQuerySession>)theStore).RewindSubscriptionProgressAsync(
            (IEventDatabase)theStore.Storage.Database, "IndexedAccount", TestContext.Current.CancellationToken,
            row.FirstFailingSequence);

        (await parkedAsync("IndexedAccount")).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task ejecting_the_shard_deletes_its_parks_and_index_rows()
    {
        var (daemon, _) = await aParkedAccountAsync();
        using var _d = daemon;

        await ((IEventDatabase)theStore.Storage.Database).DeleteProjectionProgressByShardNameAsync(
            "IndexedAccount:All", TestContext.Current.CancellationToken);

        (await parkedAsync("IndexedAccount")).ShouldBeEmpty();
        (await countAsync("mt_slice_index_indexedaccount_v1")).ShouldBe(0);
    }

    [Fact]
    public async Task removing_tenants_deletes_their_parks_and_index_rows()
    {
        var (daemon, _) = await aParkedAccountAsync();
        using var _d = daemon;

        await ((MartenDatabase)theStore.Storage.Database).DeleteParkedAndSliceIndexRowsForTenantsAsync(
            ["someone-else"], TestContext.Current.CancellationToken);
        (await parkedAsync("IndexedAccount")).ShouldHaveSingleItem();
        (await countAsync("mt_slice_index_indexedaccount_v1")).ShouldBe(4);

        await ((MartenDatabase)theStore.Storage.Database).DeleteParkedAndSliceIndexRowsForTenantsAsync(
            [StorageConstants.DefaultTenantId], TestContext.Current.CancellationToken);
        (await parkedAsync("IndexedAccount")).ShouldBeEmpty();
        (await countAsync("mt_slice_index_indexedaccount_v1")).ShouldBe(0);
    }

    [Fact]
    public async Task deleting_all_event_data_deletes_the_parks_and_index_rows()
    {
        var (daemon, _) = await aParkedAccountAsync();
        using var _d = daemon;

        await theStore.Advanced.Clean.DeleteAllEventDataAsync(TestContext.Current.CancellationToken);

        (await parkedAsync("IndexedAccount")).ShouldBeEmpty();
        (await countAsync("mt_slice_index_indexedaccount_v1")).ShouldBe(0);
    }

    [Fact]
    public async Task a_durable_request_from_any_process_marks_the_rows()
    {
        var (daemon, parked) = await aParkedAccountAsync();
        using var _d = daemon;

        var database = (IEventDatabase)theStore.Storage.Database;
        (await database.RequestParkedActionAsync(new ParkedActionRequest
        {
            ProjectionName = "IndexedAccount", Action = ParkedAction.Discard
        }, TestContext.Current.CancellationToken)).ShouldBe(1);

        var row = (await parkedAsync("IndexedAccount")).Single();
        row.RequestedAction.ShouldBe(ParkedAction.Discard);
        row.RequestedAt.ShouldNotBeNull();
        row.Identity.ShouldBe(parked);

        (await database.FetchParkedCountsAsync(null, TestContext.Current.CancellationToken)).ShouldHaveSingleItem()
            .ShouldBe(new ParkedShardCount("IndexedAccount", 1, "All", StorageConstants.DefaultTenantId, 1));
    }

    // ---- the stepper reads tags from wire records ----

    [Fact]
    public async Task stepping_a_tag_grouped_projection_from_event_records_finds_its_identity()
    {
        StoreOptions(opts =>
        {
            opts.Events.AddEventType<AccountPosted>();
            opts.Events.RegisterTagType<AccountTag>("account");
            opts.Projections.Add(new TaggedAccountProjection(), ProjectionLifecycle.Async);
        });

        var account = Guid.NewGuid();
        var other = Guid.NewGuid();

        EventRecord record(long sequence, Guid tagged) => new(Guid.NewGuid(), sequence, 1,
            Guid.NewGuid().ToString(), "account_posted",
            System.Text.Json.JsonSerializer.SerializeToElement(new { Account = tagged }), null,
            DateTimeOffset.UtcNow, null, new Dictionary<string, string> { ["account"] = tagged.ToString() });

        var timeline = await ((IEventStore)theStore).RunMultiStreamProjectionForIdentityAsync("TaggedAccount", account,
            [record(1, account), record(2, other), record(3, account)], null, TestContext.Current.CancellationToken);

        // Only the account's own events, which the slicer could only give it through their tags
        timeline.Steps.Select(x => x.Event.Sequence).ShouldBe([1L, 3L]);
        timeline.FinalState.ShouldNotBeNull().GetProperty("Postings").GetInt32().ShouldBe(2);
    }

    // jasperfx#1254: the record's tags go back onto the event through JasperFx's WithRecordMetadata, which reads a
    // key as the tag type's CLR name or its table suffix, and refuses a value it can't read
    private static EventRecord taggedRecord(long sequence, Guid account, string key, string value) => new(
        Guid.NewGuid(), sequence, 1, Guid.NewGuid().ToString(), "account_posted",
        System.Text.Json.JsonSerializer.SerializeToElement(new { Account = account }), null,
        DateTimeOffset.UtcNow, null, new Dictionary<string, string> { [key] = value });

    private void taggedAccountStore() => StoreOptions(opts =>
    {
        opts.Events.AddEventType<AccountPosted>();
        opts.Events.RegisterTagType<AccountTag>("account");
        opts.Projections.Add(new TaggedAccountProjection(), ProjectionLifecycle.Async);
    });

    [Fact]
    public async Task stepping_from_event_records_reads_a_tag_keyed_by_the_tag_types_name()
    {
        taggedAccountStore();

        var account = Guid.NewGuid();
        var other = Guid.NewGuid();

        var timeline = await ((IEventStore)theStore).RunMultiStreamProjectionForIdentityAsync("TaggedAccount", account,
            [
                taggedRecord(1, account, nameof(AccountTag), account.ToString()),
                taggedRecord(2, other, nameof(AccountTag), other.ToString())
            ], null, TestContext.Current.CancellationToken);

        timeline.Steps.Select(x => x.Event.Sequence).ShouldBe([1L]);
    }

    [Fact]
    public async Task stepping_from_an_event_record_whose_tag_value_will_not_parse_fails_naming_the_tag()
    {
        taggedAccountStore();

        var account = Guid.NewGuid();

        var ex = await Should.ThrowAsync<ArgumentException>(() =>
            ((IEventStore)theStore).RunMultiStreamProjectionForIdentityAsync("TaggedAccount", account,
                [taggedRecord(1, account, "account", "not-a-guid")], null, TestContext.Current.CancellationToken));

        ex.Message.ShouldContain("'account' = 'not-a-guid'");
    }

    // ---- parked_count in extended progression ----

    [Fact]
    public async Task the_parked_count_is_written_with_the_heartbeat_and_read_back()
    {
        var (daemon, _) = await aParkedAccountAsync();
        using var _d = daemon;

        // Running again, so the shard's heartbeat writes its parked set's size
        await daemon.StartAllAsync();

        var database = (IEventDatabase)theStore.Storage.Database;
        ShardState? shard = null;
        var deadline = DateTimeOffset.UtcNow.Add(Generous);
        while (DateTimeOffset.UtcNow < deadline)
        {
            shard = (await database.AllProjectionProgress(TestContext.Current.CancellationToken))
                .SingleOrDefault(x => x.ShardName == "IndexedAccount:All");
            if (shard?.ParkedCount == 1) break;
            await Task.Delay(250, TestContext.Current.CancellationToken);
        }

        shard.ShouldNotBeNull().ParkedCount.ShouldBe(1);
        (await countAsync("mt_event_progression", "name = 'IndexedAccount:All' and parked_count = 1")).ShouldBe(1);

        await daemon.StopAllAsync();
    }

    // ---- startup logging ----

    [Fact]
    public async Task building_the_daemon_logs_how_each_async_projection_handles_a_failing_identity()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Snapshot<GuidLedger>(SnapshotLifecycle.Async);
            opts.Projections.Add(new IndexedAccountProjection(), ProjectionLifecycle.Async);
            opts.Projections.Add(new ScanOnlyAccountProjection(), ProjectionLifecycle.Async);
        });

        var logger = new ListLogger();
        using var daemon = await theStore.BuildProjectionDaemonAsync(logger: logger);

        logger.Messages.ShouldContain(x => x.Contains("GuidLedger") && x.Contains("SingleStream") && x.Contains("parks"));
        logger.Messages.ShouldContain(x => x.Contains("IndexedAccount") && x.Contains("SliceIndex") && x.Contains("parks"));
        logger.Messages.ShouldContain(x =>
            x.Contains("ScanOnlyAccount") && x.Contains("ScanOnly") && x.Contains("pauses the shard"));
    }

    private class ListLogger: ILogger
    {
        public List<string> Messages { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (Messages) Messages.Add(formatter(state, exception));
        }
    }
}
