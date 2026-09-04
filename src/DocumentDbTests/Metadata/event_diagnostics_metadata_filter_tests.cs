using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten;
using Marten.Events;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace DocumentDbTests.Metadata;

/// <summary>
/// Coverage for marten#4791 / JasperFx/CritterWatch#629 — exact-match metadata filters
/// on <see cref="IReadOnlyEventStore.QueryEventsAsync"/>'s <see cref="EventQuery"/>.
/// Each of the three new filters (<see cref="EventQuery.CorrelationId"/>,
/// <see cref="EventQuery.CausationId"/>, <see cref="EventQuery.UserName"/>) is honored only
/// when both the option is set AND the event store has the corresponding metadata column
/// enabled. When set but disabled, the query is REFUSED with a <see cref="NotSupportedException"/>
/// naming the field — the jasperfx#737 guard rail (<see cref="EventQuery.AssertFiltersAreSupported"/>).
/// Pre-#737 the filter was silently skipped, which returned unfiltered results that read as
/// filtered; the LINQ member registration in <c>EventQueryMapping</c> is gated by the same
/// flag, so the disabled column is subtracted from the declared filter set instead.
///
/// Seeding strategy mirrors the doc-filter suite: 8 events, each one appended in its own
/// session so each carries distinct (CorrelationId / CausationId / UserName) session-scoped
/// metadata. Indexed 0..7 by the (corr, caus, user) binary tuple.
/// </summary>
public class event_diagnostics_metadata_filter_tests
{
    [Theory]
    // (corrFilter, causFilter, userFilter,  expectedEventIndices)
    [InlineData(null,  null, null, new[] { 0, 1, 2, 3, 4, 5, 6, 7 })] // no filter: every event
    [InlineData("c0",  null, null, new[] { 0, 1, 2, 3 })]               // corr=c0 only: 4 events
    [InlineData(null,  "u0", null, new[] { 0, 1, 4, 5 })]               // caus=u0 only: 4 events
    [InlineData(null,  null, "n0", new[] { 0, 2, 4, 6 })]               // user=n0 only: 4 events
    [InlineData("c0",  "u0", null, new[] { 0, 1 })]                     // corr+caus
    [InlineData("c0",  null, "n0", new[] { 0, 2 })]                     // corr+user
    [InlineData(null,  "u0", "n0", new[] { 0, 4 })]                     // caus+user
    [InlineData("c0",  "u0", "n0", new[] { 0 })]                        // all three: one event
    public async Task every_filter_combo_returns_the_expected_subset(
        string? corr, string? caus, string? user, int[] expectedIndices)
    {
        const string schema = "diag4791_evt_combo";
        using var host = await BuildHostWithAllMetadataEnabled(schema);
        await seedMatrixOfEightEvents(host);

        var store = host.Services.GetRequiredService<IDocumentStore>();
        var readStore = ((IEventStore)store).OpenReadOnlyEventStore();

        var result = await readStore.QueryEventsAsync(new EventQuery
        {
            PageNumber = 1,
            PageSize = 50,
            CorrelationId = corr,
            CausationId = caus,
            UserName = user
        }, CancellationToken.None);

        result.TotalCount.ShouldBe(expectedIndices.Length);
        var returnedIndices = result.Events
            .Select(e => ((EventMetaPayload)e.Data).Index)
            .OrderBy(i => i)
            .ToList();
        returnedIndices.ShouldBe(expectedIndices.OrderBy(i => i).ToList());
    }

    [Fact]
    public async Task filter_on_an_unmatched_value_returns_zero_events()
    {
        const string schema = "diag4791_evt_unmatched";
        using var host = await BuildHostWithAllMetadataEnabled(schema);
        await seedMatrixOfEightEvents(host);

        var store = host.Services.GetRequiredService<IDocumentStore>();
        var readStore = ((IEventStore)store).OpenReadOnlyEventStore();

        var result = await readStore.QueryEventsAsync(new EventQuery
        {
            PageNumber = 1,
            PageSize = 50,
            CorrelationId = "no-such-correlation"
        }, CancellationToken.None);

        result.TotalCount.ShouldBe(0);
        result.Events.Count.ShouldBe(0);
    }

    [Fact]
    public async Task filter_is_refused_by_name_when_correlation_id_column_is_disabled()
    {
        const string schema = "diag4791_evt_corr_off";
        using var host = await BuildHost(schema, enableCorr: false, enableCaus: true, enableUser: true);
        await seedMatrixOfEightEvents(host);

        var store = host.Services.GetRequiredService<IDocumentStore>();
        var readStore = ((IEventStore)store).OpenReadOnlyEventStore();

        // jasperfx#737: a store that does not capture the column must refuse the filter by name,
        // never silently return unfiltered results that read as filtered.
        var ex = await Should.ThrowAsync<NotSupportedException>(() => readStore.QueryEventsAsync(new EventQuery
        {
            PageNumber = 1,
            PageSize = 50,
            CorrelationId = "c0"
        }, CancellationToken.None));

        ex.Message.ShouldContain(nameof(EventQuery.CorrelationId));
    }

    [Fact]
    public async Task filter_is_refused_by_name_when_causation_id_column_is_disabled()
    {
        const string schema = "diag4791_evt_caus_off";
        using var host = await BuildHost(schema, enableCorr: true, enableCaus: false, enableUser: true);
        await seedMatrixOfEightEvents(host);

        var store = host.Services.GetRequiredService<IDocumentStore>();
        var readStore = ((IEventStore)store).OpenReadOnlyEventStore();

        var ex = await Should.ThrowAsync<NotSupportedException>(() => readStore.QueryEventsAsync(new EventQuery
        {
            PageNumber = 1,
            PageSize = 50,
            CausationId = "u0"
        }, CancellationToken.None));

        ex.Message.ShouldContain(nameof(EventQuery.CausationId));
    }

    [Fact]
    public async Task filter_is_refused_by_name_when_user_name_column_is_disabled()
    {
        const string schema = "diag4791_evt_user_off";
        using var host = await BuildHost(schema, enableCorr: true, enableCaus: true, enableUser: false);
        await seedMatrixOfEightEvents(host);

        var store = host.Services.GetRequiredService<IDocumentStore>();
        var readStore = ((IEventStore)store).OpenReadOnlyEventStore();

        var ex = await Should.ThrowAsync<NotSupportedException>(() => readStore.QueryEventsAsync(new EventQuery
        {
            PageNumber = 1,
            PageSize = 50,
            UserName = "n0"
        }, CancellationToken.None));

        ex.Message.ShouldContain(nameof(EventQuery.UserName));
    }

    [Fact]
    public async Task a_disabled_column_filter_alongside_enabled_ones_still_refuses_the_whole_query()
    {
        const string schema = "diag4791_evt_mixed";
        using var host = await BuildHost(schema, enableCorr: true, enableCaus: false, enableUser: true);
        await seedMatrixOfEightEvents(host);

        var store = host.Services.GetRequiredService<IDocumentStore>();
        var readStore = ((IEventStore)store).OpenReadOnlyEventStore();

        // Pre-#737 this returned the two events matching the enabled filters while silently
        // dropping the disabled one — a partially-filtered answer that read as fully filtered.
        // The guard rail refuses the query outright, naming only the unsupported field.
        var ex = await Should.ThrowAsync<NotSupportedException>(() => readStore.QueryEventsAsync(new EventQuery
        {
            PageNumber = 1,
            PageSize = 50,
            CorrelationId = "c0",   // supported — the column is captured
            CausationId = "u0",     // NOT captured — this is the refusal
            UserName = "n0"         // supported
        }, CancellationToken.None));

        ex.Message.ShouldContain(nameof(EventQuery.CausationId));
        ex.Message.ShouldNotContain(nameof(EventQuery.CorrelationId));
    }

    [Fact]
    public async Task existing_EventTypeName_and_StreamKey_filters_compose_with_AND_metadata_filters()
    {
        // The new metadata filters layer onto the existing EventTypeName + StreamKey filters
        // — verify the implementation ANDs them rather than overwriting either side.
        const string schema = "diag4791_evt_compose";
        using var host = await BuildHostWithAllMetadataEnabled(schema);
        await seedMatrixOfEightEvents(host);

        var store = host.Services.GetRequiredService<IDocumentStore>();
        var readStore = ((IEventStore)store).OpenReadOnlyEventStore();

        // Narrow by both event type AND CorrelationId=c0 — both should apply, returning 4 events.
        var typeAndCorr = await readStore.QueryEventsAsync(new EventQuery
        {
            PageNumber = 1,
            PageSize = 50,
            EventTypeName = "event_meta_payload",
            CorrelationId = "c0"
        }, CancellationToken.None);
        typeAndCorr.TotalCount.ShouldBe(4);

        // Same as above but with an event type that no seeded event matches → empty.
        var noSuchType = await readStore.QueryEventsAsync(new EventQuery
        {
            PageNumber = 1,
            PageSize = 50,
            EventTypeName = "not_a_real_event",
            CorrelationId = "c0"
        }, CancellationToken.None);
        noSuchType.TotalCount.ShouldBe(0);
    }

    private static Task<IHost> BuildHostWithAllMetadataEnabled(string schema) =>
        BuildHost(schema, enableCorr: true, enableCaus: true, enableUser: true);

    private static async Task<IHost> BuildHost(string schema, bool enableCorr, bool enableCaus, bool enableUser)
    {
        await using (var conn = new NpgsqlConnection(ConnectionSource.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.DropSchemaAsync(schema);
            await conn.CloseAsync();
        }

        return await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddMarten(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DatabaseSchemaName = schema;
                    opts.Events.StreamIdentity = StreamIdentity.AsString;

                    if (enableCorr) opts.Events.MetadataConfig.CorrelationIdEnabled = true;
                    if (enableCaus) opts.Events.MetadataConfig.CausationIdEnabled = true;
                    if (enableUser) opts.Events.MetadataConfig.UserNameEnabled = true;

                    opts.Events.AddEventType<EventMetaPayload>();
                });
            })
            .StartAsync();
    }

    /// <summary>
    /// Append 8 events whose metadata cover every (correlation × causation × user) combo.
    /// One session per event so each carries its own session-scoped metadata at SaveChanges time.
    /// Events share one stream — order doesn't matter because filters dispatch on metadata columns,
    /// not stream position.
    /// </summary>
    private static async Task seedMatrixOfEightEvents(IHost host)
    {
        var store = host.Services.GetRequiredService<IDocumentStore>();
        var streamKey = "metadata-matrix-" + Guid.NewGuid().ToString("N");

        for (var i = 0; i < 8; i++)
        {
            await using var session = store.LightweightSession();
            session.CorrelationId = (i & 0b100) == 0 ? "c0" : "c1";
            session.CausationId = (i & 0b010) == 0 ? "u0" : "u1";
            session.LastModifiedBy = (i & 0b001) == 0 ? "n0" : "n1";

            session.Events.Append(streamKey, new EventMetaPayload { Index = i });
            await session.SaveChangesAsync();
        }
    }
}

public class EventMetaPayload
{
    public int Index { get; set; }
}
