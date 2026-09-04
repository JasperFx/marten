#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using JasperFx.Events;
using JasperFx.Events.Documents;
using JasperFx.Events.Projections;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests;

/// <summary>
/// The refusal shapes of <c>IReadOnlyEventStore.QueryStreamStates()</c> (jasperfx#740 /
/// marten#5333) that the shared <c>StreamStateQueryCompliance</c> suite deliberately cannot pin,
/// because both current stores translate every <see cref="StreamState"/> member: an expression the
/// provider cannot translate must FAIL naming the member or operator — never silently match all
/// rows — and a tenant scope on a store with no tenant dimension must be refused, never answered
/// with unscoped rows that read as tenant-scoped.
/// </summary>
public class query_stream_states_refusals: OneOffConfigurationsContext
{
    private IQueryable<StreamState> streams()
        => ((IReadOnlyEventStore)theSession.Events).QueryStreamStates();

    private async Task<Guid> seedOneStreamAsync()
    {
        var streamId = Guid.NewGuid();
        theSession.Events.StartStream(streamId, new AEvent(), new BEvent());
        await theSession.SaveChangesAsync();
        return streamId;
    }

    [Fact]
    public void a_tenant_scope_on_a_tenantless_store_is_refused_by_name()
    {
        // The default store has no tenant dimension (TenancyStyle.Single), so a tenant-scoped
        // stream query cannot be honored and must not quietly return every tenant's rows.
        var ex = Should.Throw<NotSupportedException>(() =>
            ((IReadOnlyEventStore)theSession.Events).QueryStreamStates("tenant-a"));

        ex.Message.ShouldContain("tenant-a");
        ex.Message.ShouldContain("tenant");
    }

    [Fact]
    public async Task an_untranslatable_member_expression_is_refused_not_silently_matched()
    {
        await seedOneStreamAsync();

        // A nested member the provider does not translate. The danger shape is returning ALL rows
        // as if the predicate had matched — the seeded stream makes that detectable.
        var query = streams().Where(x => x.Key!.Length > 0);

        var ex = await Should.ThrowAsync<NotSupportedException>(
            () => DocumentQueryableExtensions.ToListAsync(query, CancellationToken.None));

        ex.Message.ShouldContain("QueryStreamStates");
    }

    [Fact]
    public void a_projection_is_refused_at_composition_time()
    {
        var ex = Should.Throw<NotSupportedException>(() => streams().Select(x => x.Version));

        ex.Message.ShouldContain("Select");
    }

    [Fact]
    public async Task an_unsupported_operator_is_refused_by_name()
    {
        var query = streams().Distinct();

        var ex = await Should.ThrowAsync<NotSupportedException>(
            () => DocumentQueryableExtensions.ToListAsync(query, CancellationToken.None));

        ex.Message.ShouldContain("Distinct");
    }

    [Fact]
    public async Task aggregate_type_supports_only_equality_comparison()
    {
        var query = streams().OrderBy(x => x.AggregateType);

        var ex = await Should.ThrowAsync<NotSupportedException>(
            () => DocumentQueryableExtensions.ToListAsync(query, CancellationToken.None));

        ex.Message.ShouldContain(nameof(StreamState.AggregateType));
    }

    [Fact]
    public void synchronous_execution_is_refused_with_guidance()
    {
        var ex = Should.Throw<NotSupportedException>(() => streams().ToList());

        ex.Message.ShouldContain("ToListAsync");
    }
}

/// <summary>
/// The compaction watermark on a STRING-identified store — the identity branch of
/// <c>RecordCompactionWatermarkOperation</c> the shared compliance suite does not reach (its
/// compaction facts run on the Guid-identified store; its string-store fact does not compact).
/// </summary>
public class compaction_watermark_on_string_identified_streams: OneOffConfigurationsContext
{
    [Fact]
    public async Task partial_then_full_compaction_move_the_watermark()
    {
        StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Projections.Add<LetterCountsByStringProjection>(ProjectionLifecycle.Inline);
        });

        var streamKey = Guid.NewGuid().ToString();
        theSession.Events.StartStream<LetterCountsByString>(streamKey,
            new AEvent(), new BEvent(), new AEvent(), new CEvent(), new CEvent(),
            new DEvent(), new DEvent(), new AEvent(), new AEvent());
        await theSession.SaveChangesAsync();

        // Partial: fold versions 1..5 into the snapshot — the watermark is the cutoff.
        await theSession.Events.CompactStreamAsync<LetterCountsByString>(streamKey, x => x.Version = 5);
        await theSession.SaveChangesAsync();

        var partial = await theSession.Events.FetchStreamStateAsync(streamKey);
        partial.Version.ShouldBe(9);
        partial.CompactedVersion.ShouldBe(5);

        // And the queryable agrees, including the growth arithmetic the compaction policies use.
        var streams = ((IReadOnlyEventStore)theSession.Events).QueryStreamStates();
        var matched = await DocumentQueryableExtensions.ToListAsync(
            streams.Where(x => x.Key == streamKey && x.Version - x.CompactedVersion > 3),
            CancellationToken.None);
        matched.ShouldHaveSingleItem().CompactedVersion.ShouldBe(5);

        // Full: the watermark advances to the stream version, so growth reads zero.
        await theSession.Events.CompactStreamAsync<LetterCountsByString>(streamKey);
        await theSession.SaveChangesAsync();

        var full = await theSession.Events.FetchStreamStateAsync(streamKey);
        full.Version.ShouldBe(9);
        full.CompactedVersion.ShouldBe(9);

        var grown = await DocumentQueryableExtensions.CountAsync(
            streams.Where(x => x.Key == streamKey && x.Version - x.CompactedVersion > 0),
            CancellationToken.None);
        grown.ShouldBe(0);
    }
}
