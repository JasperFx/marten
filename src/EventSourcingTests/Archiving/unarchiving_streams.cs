using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Projections;
using JasperFx.Events.Tags;
using Marten;
using Marten.Events;
using Marten.Exceptions;
using Marten.Storage;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql;
using Xunit;

namespace EventSourcingTests.Archiving;

/// <summary>
///     marten#5476: <c>IEventStoreOperations.UnArchiveStream</c>, the reverse of <c>ArchiveStream</c>. The shared
///     <c>StreamArchivingCompliance</c> suite (enrolled as <c>stream_archiving_compliance</c>) pins the contract on
///     the default configuration; these facts pin the Postgres storage shapes it does not reach: archived-stream
///     partitioning, conjoined tenancy, string identity, both append modes, DCB tag rows, natural keys, inline and
///     async projections, and the identity reuse that only the partitioned layout allows.
/// </summary>
public class unarchiving_streams: OneOffConfigurationsContext
{
    public record LedgerOpened(string Name);

    public record LedgerPosted(decimal Amount);

    public record LedgerId(Guid Value);

    public record LedgerNumber(string Value);

    public record NumberedLedgerOpened(string Number);

    public class Ledger
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal Balance { get; set; }

        public static Ledger Create(LedgerOpened e) => new() { Name = e.Name };
        public void Apply(LedgerPosted e) => Balance += e.Amount;
    }

    public class NumberedLedger
    {
        public Guid Id { get; set; }

        [NaturalKey]
        public LedgerNumber Number { get; set; } = null!;

        public decimal Balance { get; set; }

        [NaturalKeySource]
        public static NumberedLedger Create(IEvent<NumberedLedgerOpened> e) =>
            new() { Id = e.StreamId, Number = new LedgerNumber(e.Data.Number) };

        public void Apply(LedgerPosted e) => Balance += e.Amount;
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public async Task unarchiving_restores_the_stream_and_its_events(bool stringIdentity, bool conjoined,
        bool partitioned)
    {
        StoreOptions(opts =>
        {
            opts.Events.UseArchivedStreamPartitioning = partitioned;
            opts.Events.StreamIdentity = stringIdentity ? StreamIdentity.AsString : StreamIdentity.AsGuid;
            opts.Events.TenancyStyle = conjoined ? TenancyStyle.Conjoined : TenancyStyle.Single;
            opts.Events.AddEventType<LedgerOpened>();
            opts.Events.AddEventType<LedgerPosted>();
        });

        var tenant = conjoined ? "one" : StorageConstants.DefaultTenantId;
        object id = stringIdentity ? Guid.NewGuid().ToString() : Guid.NewGuid();
        object neighbour = stringIdentity ? Guid.NewGuid().ToString() : Guid.NewGuid();

        await using (var session = theStore.LightweightSession(tenant))
        {
            startStream(session, id);
            startStream(session, neighbour);
            await session.SaveChangesAsync();
        }

        // Under conjoined tenancy the same identity in another tenant is a different stream, and it is
        // archived too: un-archiving tenant "one" must not reach it
        if (conjoined)
        {
            await using var other = theStore.LightweightSession("two");
            startStream(other, id);
            await other.SaveChangesAsync();
            archive(other, id);
            await other.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession(tenant))
        {
            archive(session, id);
            archive(session, neighbour);
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession(tenant))
        {
            unarchive(session, id);
            await session.SaveChangesAsync();
        }

        await using (var query = theStore.LightweightSession(tenant))
        {
            var state = await fetchState(query, id);
            state.ShouldNotBeNull();
            state.IsArchived.ShouldBeFalse();
            state.Version.ShouldBe(2);

            var events = await fetchStream(query, id);
            events.Count.ShouldBe(2);
            events.ShouldAllBe(x => !x.IsArchived);

            // The neighbour archived alongside it stays archived
            (await fetchState(query, neighbour))!.IsArchived.ShouldBeTrue();
        }

        // Every row is back in the live state, and under partitioning nothing is left in the archived one
        (await countRows("mt_streams", id, tenant, isArchived: false)).ShouldBe(1);
        (await countRows("mt_events", id, tenant, isArchived: false)).ShouldBe(2);
        (await countRows("mt_streams", id, tenant, isArchived: true)).ShouldBe(0);
        (await countRows("mt_events", id, tenant, isArchived: true)).ShouldBe(0);

        if (conjoined)
        {
            (await countRows("mt_streams", id, "two", isArchived: true)).ShouldBe(1);
            (await countRows("mt_events", id, "two", isArchived: true)).ShouldBe(2);
            (await countRows("mt_events", id, "two", isArchived: false)).ShouldBe(0);
        }
    }

    [Theory]
    [InlineData(EventAppendMode.Rich, false)]
    [InlineData(EventAppendMode.Rich, true)]
    [InlineData(EventAppendMode.Quick, false)]
    [InlineData(EventAppendMode.Quick, true)]
    [InlineData(EventAppendMode.QuickWithServerTimestamps, false)]
    [InlineData(EventAppendMode.QuickWithServerTimestamps, true)]
    public async Task an_unarchived_stream_can_be_appended_to(EventAppendMode mode, bool partitioned)
    {
        StoreOptions(opts =>
        {
            opts.Events.AppendMode = mode;
            opts.Events.UseArchivedStreamPartitioning = partitioned;
            opts.Events.AddEventType<LedgerOpened>();
            opts.Events.AddEventType<LedgerPosted>();
        });

        var streamId = Guid.NewGuid();
        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream(streamId, new LedgerOpened("books"), new LedgerPosted(1));
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            session.Events.ArchiveStream(streamId);
            await session.SaveChangesAsync();
        }

        // Archived: the append is refused (Rich: RichEventAppender, Quick: MT001 in mt_quick_append_events)
        await using (var session = theStore.LightweightSession())
        {
            session.Events.Append(streamId, new LedgerPosted(2));
            await Should.ThrowAsync<ArchivedStreamException>(() => session.SaveChangesAsync());
        }

        await using (var session = theStore.LightweightSession())
        {
            session.Events.UnArchiveStream(streamId);
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            session.Events.Append(streamId, new LedgerPosted(3));
            await session.SaveChangesAsync();
        }

        await using var query = theStore.LightweightSession();
        var state = await query.Events.FetchStreamStateAsync(streamId);
        state.ShouldNotBeNull();
        state.Version.ShouldBe(3);
        state.IsArchived.ShouldBeFalse();

        var events = await query.Events.FetchStreamAsync(streamId);
        events.Select(x => x.Version).ShouldBe([1L, 2L, 3L]);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task an_unarchived_stream_accepts_an_append_at_its_expected_version(bool partitioned, bool quick)
    {
        StoreOptions(opts =>
        {
            opts.Events.AppendMode = quick ? EventAppendMode.Quick : EventAppendMode.Rich;
            opts.Events.UseArchivedStreamPartitioning = partitioned;
            opts.Events.AddEventType<LedgerOpened>();
            opts.Events.AddEventType<LedgerPosted>();
        });

        var streamId = Guid.NewGuid();
        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream(streamId, new LedgerOpened("books"), new LedgerPosted(1));
            await session.SaveChangesAsync();

            session.Events.ArchiveStream(streamId);
            await session.SaveChangesAsync();

            session.Events.UnArchiveStream(streamId);
            await session.SaveChangesAsync();
        }

        // The version is unchanged by the round trip, so the expectation is the version before archiving
        await using (var session = theStore.LightweightSession())
        {
            session.Events.Append(streamId, 3, new LedgerPosted(2));
            await session.SaveChangesAsync();
        }

        // And a stale expectation is still refused
        await using (var session = theStore.LightweightSession())
        {
            session.Events.Append(streamId, 3, new LedgerPosted(2));
            await Should.ThrowAsync<Exception>(() => session.SaveChangesAsync());
        }

        await using var query = theStore.LightweightSession();
        (await query.Events.FetchStreamStateAsync(streamId))!.Version.ShouldBe(3);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task raw_event_queries_see_the_events_again(bool partitioned)
    {
        StoreOptions(opts =>
        {
            opts.Events.UseArchivedStreamPartitioning = partitioned;
            opts.Events.AddEventType<LedgerOpened>();
            opts.Events.AddEventType<LedgerPosted>();
        });

        var streamId = Guid.NewGuid();
        await using var session = theStore.LightweightSession();
        session.Events.StartStream(streamId, new LedgerOpened("books"), new LedgerPosted(1));
        await session.SaveChangesAsync();

        session.Events.ArchiveStream(streamId);
        await session.SaveChangesAsync();
        (await session.Events.QueryAllRawEvents().Where(x => x.StreamId == streamId).CountAsync()).ShouldBe(0);

        session.Events.UnArchiveStream(streamId);
        await session.SaveChangesAsync();

        (await session.Events.QueryAllRawEvents().Where(x => x.StreamId == streamId).CountAsync()).ShouldBe(2);
        (await session.Events.QueryAllRawEvents().Where(x => x.StreamId == streamId && x.IsArchived).CountAsync())
            .ShouldBe(0);
        (await session.Events.QueryRawEventDataOnly<LedgerPosted>().CountAsync()).ShouldBeGreaterThan(0);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task a_stream_can_be_archived_again_after_unarchiving(bool partitioned, bool stringIdentity)
    {
        StoreOptions(opts =>
        {
            opts.Events.UseArchivedStreamPartitioning = partitioned;
            opts.Events.StreamIdentity = stringIdentity ? StreamIdentity.AsString : StreamIdentity.AsGuid;
            opts.Events.AddEventType<LedgerOpened>();
            opts.Events.AddEventType<LedgerPosted>();
        });

        object id = stringIdentity ? Guid.NewGuid().ToString() : Guid.NewGuid();
        var tenant = StorageConstants.DefaultTenantId;

        await using var session = theStore.LightweightSession();
        startStream(session, id);
        await session.SaveChangesAsync();

        for (var i = 0; i < 2; i++)
        {
            archive(session, id);
            await session.SaveChangesAsync();
            (await fetchState(session, id))!.IsArchived.ShouldBeTrue();

            unarchive(session, id);
            await session.SaveChangesAsync();
            (await fetchState(session, id))!.IsArchived.ShouldBeFalse();
        }

        archive(session, id);
        await session.SaveChangesAsync();

        (await fetchState(session, id))!.IsArchived.ShouldBeTrue();
        (await countRows("mt_events", id, tenant, isArchived: true)).ShouldBe(2);
        (await countRows("mt_events", id, tenant, isArchived: false)).ShouldBe(0);
        (await countRows("mt_streams", id, tenant, isArchived: true)).ShouldBe(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task unarchiving_in_the_same_session_as_the_archive_leaves_the_stream_live(bool partitioned)
    {
        StoreOptions(opts =>
        {
            opts.Events.UseArchivedStreamPartitioning = partitioned;
            opts.Events.AddEventType<LedgerOpened>();
        });

        var streamId = Guid.NewGuid();
        await using var session = theStore.LightweightSession();
        session.Events.StartStream(streamId, new LedgerOpened("books"));
        await session.SaveChangesAsync();

        // Queued operations run in the order they were queued
        session.Events.ArchiveStream(streamId);
        session.Events.UnArchiveStream(streamId);
        await session.SaveChangesAsync();

        var state = await session.Events.FetchStreamStateAsync(streamId);
        state.ShouldNotBeNull();
        state.IsArchived.ShouldBeFalse();
        (await session.Events.FetchStreamAsync(streamId)).Count.ShouldBe(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task unarchiving_a_live_stream_is_a_no_op(bool partitioned)
    {
        StoreOptions(opts =>
        {
            opts.Events.UseArchivedStreamPartitioning = partitioned;
            opts.Events.AddEventType<LedgerOpened>();
            opts.Events.AddEventType<LedgerPosted>();
        });

        var streamId = Guid.NewGuid();
        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream(streamId, new LedgerOpened("books"), new LedgerPosted(1));
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            session.Events.UnArchiveStream(streamId);
            session.Events.UnArchiveStream(streamId);
            await session.SaveChangesAsync();
        }

        await using var query = theStore.LightweightSession();
        var state = await query.Events.FetchStreamStateAsync(streamId);
        state.ShouldNotBeNull();
        state.IsArchived.ShouldBeFalse();
        state.Version.ShouldBe(2);

        (await countRows("mt_streams", streamId, StorageConstants.DefaultTenantId, isArchived: false)).ShouldBe(1);
        (await countRows("mt_events", streamId, StorageConstants.DefaultTenantId, isArchived: false)).ShouldBe(2);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task unarchiving_a_stream_that_does_not_exist_is_a_no_op(bool stringIdentity, bool partitioned)
    {
        StoreOptions(opts =>
        {
            opts.Events.UseArchivedStreamPartitioning = partitioned;
            opts.Events.StreamIdentity = stringIdentity ? StreamIdentity.AsString : StreamIdentity.AsGuid;
            opts.Events.AddEventType<LedgerOpened>();
        });

        object id = stringIdentity ? Guid.NewGuid().ToString() : Guid.NewGuid();

        await using (var session = theStore.LightweightSession())
        {
            unarchive(session, id);
            await session.SaveChangesAsync();
        }

        // Nothing was created by it
        await using var query = theStore.LightweightSession();
        (await fetchState(query, id)).ShouldBeNull();
    }

    [Fact]
    public void unarchiving_with_the_wrong_identity_kind_is_refused()
    {
        StoreOptions(opts => opts.Events.StreamIdentity = StreamIdentity.AsGuid);

        using var session = theStore.LightweightSession();
        Should.Throw<InvalidOperationException>(() => session.Events.UnArchiveStream("not-a-guid-stream"));
    }

    [Fact]
    public void unarchiving_with_the_wrong_identity_kind_is_refused_for_string_streams()
    {
        StoreOptions(opts => opts.Events.StreamIdentity = StreamIdentity.AsString);

        using var session = theStore.LightweightSession();
        Should.Throw<InvalidOperationException>(() => session.Events.UnArchiveStream(Guid.NewGuid()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task unarchiving_is_refused_when_the_identity_was_reused_by_a_live_stream(bool conjoined)
    {
        // Only the partitioned layout lets an archived stream's identity be reused
        // (start_stream_with_id_of_previously_archived_stream). Bringing the archived stream back would leave
        // two live streams on one identity, colliding on every version.
        StoreOptions(opts =>
        {
            opts.Events.UseArchivedStreamPartitioning = true;

            // Strict stream identity (on by default since 10.0, #5812) refuses the reuse this test needs
            opts.Events.EnableStrictStreamIdentityEnforcement = false;
            opts.Events.TenancyStyle = conjoined ? TenancyStyle.Conjoined : TenancyStyle.Single;
            opts.Events.AddEventType<LedgerOpened>();
            opts.Events.AddEventType<LedgerPosted>();
        });

        var tenant = conjoined ? "one" : StorageConstants.DefaultTenantId;
        var streamId = Guid.NewGuid();

        await using (var session = theStore.LightweightSession(tenant))
        {
            session.Events.StartStream(streamId, new LedgerOpened("first"), new LedgerPosted(1));
            await session.SaveChangesAsync();

            session.Events.ArchiveStream(streamId);
            await session.SaveChangesAsync();

            session.Events.StartStream(streamId, new LedgerOpened("second"));
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession(tenant))
        {
            session.Events.UnArchiveStream(streamId);
            var ex = await Should.ThrowAsync<InvalidStreamOperationException>(() => session.SaveChangesAsync());
            ex.Message.ShouldContain(streamId.ToString());
        }

        // Nothing moved: the archived stream is still archived and the live one is untouched
        (await countRows("mt_streams", streamId, tenant, isArchived: true)).ShouldBe(1);
        (await countRows("mt_events", streamId, tenant, isArchived: true)).ShouldBe(2);
        (await countRows("mt_streams", streamId, tenant, isArchived: false)).ShouldBe(1);
        (await countRows("mt_events", streamId, tenant, isArchived: false)).ShouldBe(1);
    }

    [Theory]
    [InlineData(DcbStorageMode.TagTables, false, false)]
    [InlineData(DcbStorageMode.TagTables, true, false)]
    [InlineData(DcbStorageMode.TagTables, true, true)]
    [InlineData(DcbStorageMode.HStore, false, false)]
    [InlineData(DcbStorageMode.HStore, true, false)]
    public async Task unarchiving_a_tagged_stream_brings_its_tags_back(DcbStorageMode mode, bool partitioned,
        bool conjoined)
    {
        StoreOptions(opts =>
        {
            opts.Events.UseArchivedStreamPartitioning = partitioned;
            opts.Events.TenancyStyle = conjoined ? TenancyStyle.Conjoined : TenancyStyle.Single;
            opts.Events.DcbStorageMode = mode;
            opts.Events.AddEventType<LedgerOpened>();
            opts.Events.AddEventType<LedgerPosted>();
            opts.Events.RegisterTagType<LedgerId, Guid>(id => id.Value, "ledger");
        });

        var tenant = conjoined ? "one" : StorageConstants.DefaultTenantId;
        var streamId = Guid.NewGuid();
        var neighbourId = Guid.NewGuid();
        var tag = new LedgerId(Guid.NewGuid());
        var neighbourTag = new LedgerId(Guid.NewGuid());

        await using (var session = theStore.LightweightSession(tenant))
        {
            var opened = session.Events.BuildEvent(new LedgerOpened("tagged"));
            opened.WithTag(tag);
            var posted = session.Events.BuildEvent(new LedgerPosted(10));
            posted.WithTag(tag);
            session.Events.StartStream(streamId, opened, posted);

            var neighbour = session.Events.BuildEvent(new LedgerOpened("neighbour"));
            neighbour.WithTag(neighbourTag);
            session.Events.StartStream(neighbourId, neighbour);

            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession(tenant))
        {
            session.Events.ArchiveStream(streamId);
            session.Events.ArchiveStream(neighbourId);
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession(tenant))
        {
            (await session.Events.EventsExistAsync(new EventTagQuery().Or<LedgerId>(tag))).ShouldBeFalse();

            session.Events.UnArchiveStream(streamId);
            await session.SaveChangesAsync();
        }

        await using var query = theStore.LightweightSession(tenant);
        var events = await query.Events.QueryByTagsAsync(new EventTagQuery().Or<LedgerId>(tag));
        events.Count.ShouldBe(2);
        events.ShouldAllBe(x => x.StreamId == streamId);

        // The neighbour archived with it stays out of tag queries
        (await query.Events.EventsExistAsync(new EventTagQuery().Or<LedgerId>(neighbourTag))).ShouldBeFalse();

        if (mode == DcbStorageMode.TagTables && partitioned)
        {
            (await countTagRows(tag.Value, isArchived: false)).ShouldBe(2);
            (await countTagRows(tag.Value, isArchived: true)).ShouldBe(0);
            (await countTagRows(neighbourTag.Value, isArchived: true)).ShouldBe(1);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task an_unarchived_stream_resolves_by_its_natural_key_again(bool partitioned, bool conjoined)
    {
        configureNaturalKeys(partitioned, conjoined);

        var tenant = conjoined ? "one" : StorageConstants.DefaultTenantId;
        var streamId = Guid.NewGuid();
        var number = "LDG-" + Guid.NewGuid().ToString("N");

        await using (var session = theStore.LightweightSession(tenant))
        {
            session.Events.StartStream<NumberedLedger>(streamId, new NumberedLedgerOpened(number),
                new LedgerPosted(5));
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession(tenant))
        {
            session.Events.ArchiveStream(streamId);
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession(tenant))
        {
            (await session.Events.FetchLatest<NumberedLedger, LedgerNumber>(new LedgerNumber(number))).ShouldBeNull();

            session.Events.UnArchiveStream(streamId);
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession(tenant))
        {
            var ledger = await session.Events.FetchLatest<NumberedLedger, LedgerNumber>(new LedgerNumber(number));
            ledger.ShouldNotBeNull();
            ledger.Id.ShouldBe(streamId);
            ledger.Balance.ShouldBe(5);

            // And the key is still owned by it: writing through it works
            var stream = await session.Events.FetchForWriting<NumberedLedger, LedgerNumber>(new LedgerNumber(number));
            stream.Aggregate.ShouldNotBeNull();
            stream.AppendOne(new LedgerPosted(1));
            await session.SaveChangesAsync();
        }

        await using var query = theStore.LightweightSession(tenant);
        (await query.Events.FetchLatest<NumberedLedger, LedgerNumber>(new LedgerNumber(number)))!
            .Balance.ShouldBe(6);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task a_natural_key_claimed_while_archived_stays_with_its_new_owner(bool partitioned)
    {
        configureNaturalKeys(partitioned, conjoined: false);

        var original = Guid.NewGuid();
        var replacement = Guid.NewGuid();
        var number = "LDG-" + Guid.NewGuid().ToString("N");

        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream<NumberedLedger>(original, new NumberedLedgerOpened(number));
            await session.SaveChangesAsync();

            session.Events.ArchiveStream(original);
            await session.SaveChangesAsync();

            // An archived stream's key is free to claim (natural_key_uniqueness)
            session.Events.StartStream<NumberedLedger>(replacement, new NumberedLedgerOpened(number));
            await session.SaveChangesAsync();

            session.Events.UnArchiveStream(original);
            await session.SaveChangesAsync();
        }

        await using var query = theStore.LightweightSession();
        (await query.Events.FetchStreamStateAsync(original))!.IsArchived.ShouldBeFalse();

        var ledger = await query.Events.FetchLatest<NumberedLedger, LedgerNumber>(new LedgerNumber(number));
        ledger.ShouldNotBeNull();
        ledger.Id.ShouldBe(replacement);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task archiving_two_holders_of_one_natural_key_in_turn_succeeds(bool conjoined)
    {
        // Under partitioning mt_archive_stream now copies the lookup row into the archived partition. A second
        // archived holder of the same key collides with the first one's copy there, which must not fail the
        // archive: the first copy is kept, and un-archiving the first holder brings its key back.
        configureNaturalKeys(partitioned: true, conjoined);

        var tenant = conjoined ? "one" : StorageConstants.DefaultTenantId;
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var number = "LDG-" + Guid.NewGuid().ToString("N");

        await using (var session = theStore.LightweightSession(tenant))
        {
            session.Events.StartStream<NumberedLedger>(first, new NumberedLedgerOpened(number));
            await session.SaveChangesAsync();
            session.Events.ArchiveStream(first);
            await session.SaveChangesAsync();

            session.Events.StartStream<NumberedLedger>(second, new NumberedLedgerOpened(number));
            await session.SaveChangesAsync();
            session.Events.ArchiveStream(second);
            await session.SaveChangesAsync();

            (await session.Events.FetchStreamStateAsync(second))!.IsArchived.ShouldBeTrue();

            session.Events.UnArchiveStream(first);
            await session.SaveChangesAsync();
        }

        await using var query = theStore.LightweightSession(tenant);
        var ledger = await query.Events.FetchLatest<NumberedLedger, LedgerNumber>(new LedgerNumber(number));
        ledger.ShouldNotBeNull();
        ledger.Id.ShouldBe(first);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task live_aggregation_and_fetch_for_writing_see_the_events_again(bool partitioned)
    {
        StoreOptions(opts =>
        {
            opts.Events.UseArchivedStreamPartitioning = partitioned;
            opts.Events.AddEventType<LedgerOpened>();
            opts.Events.AddEventType<LedgerPosted>();
            opts.Projections.LiveStreamAggregation<Ledger>();
        });

        var streamId = Guid.NewGuid();
        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream<Ledger>(streamId, new LedgerOpened("books"), new LedgerPosted(7));
            await session.SaveChangesAsync();

            session.Events.ArchiveStream(streamId);
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            (await session.Events.AggregateStreamAsync<Ledger>(streamId)).ShouldBeNull();

            session.Events.UnArchiveStream(streamId);
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            var ledger = await session.Events.AggregateStreamAsync<Ledger>(streamId);
            ledger.ShouldNotBeNull();
            ledger.Balance.ShouldBe(7);

            var stream = await session.Events.FetchForWriting<Ledger>(streamId);
            stream.Aggregate.ShouldNotBeNull();
            stream.Aggregate.Balance.ShouldBe(7);
            stream.CurrentVersion.ShouldBe(2);
            stream.AppendOne(new LedgerPosted(3));
            await session.SaveChangesAsync();
        }

        await using var query = theStore.LightweightSession();
        (await query.Events.FetchLatest<Ledger>(streamId))!.Balance.ShouldBe(10);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task an_inline_snapshot_is_neither_touched_by_unarchiving_nor_rewound(bool partitioned)
    {
        StoreOptions(opts =>
        {
            opts.Events.UseArchivedStreamPartitioning = partitioned;
            opts.Projections.Snapshot<Ledger>(SnapshotLifecycle.Inline);
        });

        var streamId = Guid.NewGuid();
        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream<Ledger>(streamId, new LedgerOpened("books"), new LedgerPosted(4));
            await session.SaveChangesAsync();

            session.Events.ArchiveStream(streamId);
            await session.SaveChangesAsync();

            // ArchiveStream queues no event, so the inline projection never heard of it and the
            // snapshot is still there
            (await session.LoadAsync<Ledger>(streamId))!.Balance.ShouldBe(4);

            session.Events.UnArchiveStream(streamId);
            await session.SaveChangesAsync();

            (await session.LoadAsync<Ledger>(streamId))!.Balance.ShouldBe(4);

            session.Events.Append(streamId, new LedgerPosted(6));
            await session.SaveChangesAsync();
        }

        await using var query = theStore.QuerySession();
        (await query.LoadAsync<Ledger>(streamId))!.Balance.ShouldBe(10);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task an_async_projection_picks_up_new_events_after_unarchiving(bool partitioned)
    {
        StoreOptions(opts =>
        {
            opts.Events.UseArchivedStreamPartitioning = partitioned;
            opts.Projections.Snapshot<Ledger>(SnapshotLifecycle.Async);
        });

        var streamId = Guid.NewGuid();
        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream<Ledger>(streamId, new LedgerOpened("books"), new LedgerPosted(2));
            await session.SaveChangesAsync();
        }

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();
        await daemon.WaitForNonStaleData(30.Seconds());

        await using (var session = theStore.LightweightSession())
        {
            session.Events.ArchiveStream(streamId);
            await session.SaveChangesAsync();

            session.Events.UnArchiveStream(streamId);
            await session.SaveChangesAsync();

            session.Events.Append(streamId, new LedgerPosted(5));
            await session.SaveChangesAsync();
        }

        await daemon.WaitForNonStaleData(30.Seconds());

        // The restored events keep their sequence numbers, which the daemon had already passed, so they are
        // not delivered a second time: only the new event is applied, on top of the existing snapshot.
        await using var query = theStore.QuerySession();
        (await query.LoadAsync<Ledger>(streamId))!.Balance.ShouldBe(7);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task the_unarchive_function_is_not_reported_as_drift(bool partitioned, bool conjoined)
    {
        // mt_unarchive_stream is a new schema object, and mt_archive_stream's body now names the natural key
        // tables under partitioning: neither may read as a pending change once applied.
        StoreOptions(opts =>
        {
            opts.Events.UseArchivedStreamPartitioning = partitioned;
            opts.Events.TenancyStyle = conjoined ? TenancyStyle.Conjoined : TenancyStyle.Single;
            if (conjoined) opts.Policies.AllDocumentsAreMultiTenanted();
            opts.Events.RegisterTagType<LedgerId, Guid>(id => id.Value, "ledger");
            opts.RegisterValueType(typeof(LedgerNumber));
            opts.Projections.Snapshot<NumberedLedger>(SnapshotLifecycle.Inline);
        });

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        await theStore.Storage.Database.AssertDatabaseMatchesConfigurationAsync();
    }

    [Fact]
    public async Task sample_usage()
    {
        StoreOptions(opts => opts.Events.AddEventType<LedgerOpened>());

        var streamId = Guid.NewGuid();
        await using (var start = theStore.LightweightSession())
        {
            start.Events.StartStream(streamId, new LedgerOpened("reopened"));
            await start.SaveChangesAsync();

            start.Events.ArchiveStream(streamId);
            await start.SaveChangesAsync();
        }

        #region sample_unarchive_stream

        await using var session = theStore.LightweightSession();

        // Queue the un-archive. Like ArchiveStream, nothing touches the
        // database until SaveChangesAsync
        session.Events.UnArchiveStream(streamId);

        // The string-keyed overload is used when StreamIdentity is AsString:
        // session.Events.UnArchiveStream("customer-123");

        await session.SaveChangesAsync();

        // The stream and its events are live again, and can be appended to
        session.Events.Append(streamId, new LedgerOpened("appended after un-archiving"));
        await session.SaveChangesAsync();

        #endregion

        (await session.Events.FetchStreamStateAsync(streamId))!.Version.ShouldBe(2);
    }

    private void configureNaturalKeys(bool partitioned, bool conjoined)
    {
        StoreOptions(opts =>
        {
            opts.Events.UseArchivedStreamPartitioning = partitioned;
            opts.Events.TenancyStyle = conjoined ? TenancyStyle.Conjoined : TenancyStyle.Single;
            if (conjoined) opts.Policies.AllDocumentsAreMultiTenanted();
            opts.RegisterValueType(typeof(LedgerNumber));
            opts.Projections.Snapshot<NumberedLedger>(SnapshotLifecycle.Inline);
        });
    }

    private static void startStream(IDocumentSession session, object id)
    {
        if (id is Guid guid) session.Events.StartStream(guid, new LedgerOpened("x"), new LedgerPosted(1));
        else session.Events.StartStream((string)id, new LedgerOpened("x"), new LedgerPosted(1));
    }

    private static void archive(IDocumentSession session, object id)
    {
        if (id is Guid guid) session.Events.ArchiveStream(guid);
        else session.Events.ArchiveStream((string)id);
    }

    private static void unarchive(IDocumentSession session, object id)
    {
        if (id is Guid guid) session.Events.UnArchiveStream(guid);
        else session.Events.UnArchiveStream((string)id);
    }

    private static Task<StreamState?> fetchState(IDocumentSession session, object id)
    {
        return id is Guid guid
            ? session.Events.FetchStreamStateAsync(guid)
            : session.Events.FetchStreamStateAsync((string)id);
    }

    private static async Task<System.Collections.Generic.IReadOnlyList<IEvent>> fetchStream(IDocumentSession session,
        object id)
    {
        return id is Guid guid
            ? await session.Events.FetchStreamAsync(guid)
            : await session.Events.FetchStreamAsync((string)id);
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

    private async Task<long> countTagRows(Guid value, bool isArchived)
    {
        await using var connection = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection
            .CreateCommand(
                $"select count(*) from {SchemaName}.mt_event_tag_ledger where value = :value and is_archived = :archived")
            .With("value", value)
            .With("archived", isArchived);
        return (long)(await command.ExecuteScalarAsync())!;
    }
}
