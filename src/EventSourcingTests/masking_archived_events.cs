using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using Marten;
using Marten.Events;
using Marten.Events.Archiving;
using Marten.Storage;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace EventSourcingTests;

/// <summary>
/// Archiving a stream takes it out of the normal reads and leaves it on disk, so masking has to reach it.
/// It read through the same paths everything else does, which left archived rows out: protected data stayed
/// where it was and the pass reported success. Every assertion here reads the stored row with SQL, because
/// the read under test is the thing that could not see it.
/// </summary>
public class masking_archived_events: OneOffConfigurationsContext
{
    private const string Secret = "alice@example.com";
    private const string Masked = "*****";

    private void maskingEnabled(Action<StoreOptions>? configure = null)
    {
        StoreOptions(opts =>
        {
            opts.Events.AddMaskingRuleForProtectedInformation<QuestStarted>(x => x.Name = Masked);
            configure?.Invoke(opts);
        });
    }

    private async Task<Guid> anArchivedStream()
    {
        var streamId = theSession.Events.StartStream<Quest>(new QuestStarted { Name = Secret }).Id;
        await theSession.SaveChangesAsync();

        theSession.Events.ArchiveStream(streamId);
        await theSession.SaveChangesAsync();

        return streamId;
    }

    private async Task<string> anArchivedKeyedStream()
    {
        var streamKey = Guid.NewGuid().ToString();
        theSession.Events.StartStream<Quest>(streamKey, new QuestStarted { Name = Secret });
        await theSession.SaveChangesAsync();

        theSession.Events.ArchiveStream(streamKey);
        await theSession.SaveChangesAsync();

        return streamKey;
    }

    private async Task<string> storedRow(object streamIdentity)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"select data::text from {SchemaName}.mt_events where stream_id = @id";
        cmd.Parameters.AddWithValue("id", streamIdentity);

        var data = await cmd.ExecuteScalarAsync();
        return data.ShouldBeOfType<string>();
    }

    private async Task theRowWasMasked(object streamIdentity)
    {
        var stored = await storedRow(streamIdentity);
        stored.ShouldNotContain(Secret);
        stored.ShouldContain(Masked);
    }

    [Fact]
    public async Task include_stream_by_guid_reaches_an_archived_stream()
    {
        maskingEnabled();
        var streamId = await anArchivedStream();

        await theStore.Advanced.ApplyEventDataMasking(x => x.IncludeStream(streamId));

        await theRowWasMasked(streamId);
    }

    [Fact]
    public async Task include_stream_by_guid_with_a_filter_reaches_an_archived_stream()
    {
        maskingEnabled();
        var streamId = await anArchivedStream();

        await theStore.Advanced.ApplyEventDataMasking(x => x.IncludeStream(streamId, e => e.Version == 1));

        await theRowWasMasked(streamId);
    }

    [Fact]
    public async Task include_stream_by_key_reaches_an_archived_stream()
    {
        maskingEnabled(opts => opts.Events.StreamIdentity = StreamIdentity.AsString);
        var streamKey = await anArchivedKeyedStream();

        await theStore.Advanced.ApplyEventDataMasking(x => x.IncludeStream(streamKey));

        await theRowWasMasked(streamKey);
    }

    [Fact]
    public async Task include_stream_by_key_with_a_filter_reaches_an_archived_stream()
    {
        maskingEnabled(opts => opts.Events.StreamIdentity = StreamIdentity.AsString);
        var streamKey = await anArchivedKeyedStream();

        await theStore.Advanced.ApplyEventDataMasking(x => x.IncludeStream(streamKey, e => e.Version == 1));

        await theRowWasMasked(streamKey);
    }

    [Fact]
    public async Task include_events_reaches_an_archived_stream()
    {
        maskingEnabled();
        var streamId = await anArchivedStream();

        await theStore.Advanced.ApplyEventDataMasking(x => x.IncludeEvents(e => e.StreamId == streamId));

        await theRowWasMasked(streamId);
    }

    [Fact]
    public async Task include_events_can_still_name_the_archived_events_on_its_own()
    {
        maskingEnabled();
        var archived = await anArchivedStream();

        var live = theSession.Events.StartStream<Quest>(new QuestStarted { Name = Secret }).Id;
        await theSession.SaveChangesAsync();

        await theStore.Advanced.ApplyEventDataMasking(x => x.IncludeEvents(e => e.IsArchived));

        await theRowWasMasked(archived);
        (await storedRow(live)).ShouldContain(Secret);
    }

    [Fact]
    public async Task include_events_can_name_one_subjects_archived_events()
    {
        maskingEnabled();
        var subject = await anArchivedStream();
        var other = await anArchivedStream();

        await theStore.Advanced.ApplyEventDataMasking(x => x.IncludeEvents(e => e.StreamId == subject && e.IsArchived));

        await theRowWasMasked(subject);
        (await storedRow(other)).ShouldContain(Secret);
    }

    [Fact]
    public async Task include_events_can_still_rule_the_archived_events_out()
    {
        maskingEnabled();
        var archived = await anArchivedStream();

        var live = theSession.Events.StartStream<Quest>(new QuestStarted { Name = Secret }).Id;
        await theSession.SaveChangesAsync();

        await theStore.Advanced.ApplyEventDataMasking(x => x.IncludeEvents(e => !e.IsArchived));

        await theRowWasMasked(live);
        (await storedRow(archived)).ShouldContain(Secret);
    }

    [Fact]
    public async Task a_live_stream_is_still_masked()
    {
        maskingEnabled();

        var streamId = theSession.Events.StartStream<Quest>(new QuestStarted { Name = Secret }).Id;
        await theSession.SaveChangesAsync();

        await theStore.Advanced.ApplyEventDataMasking(x => x.IncludeStream(streamId));

        await theRowWasMasked(streamId);
    }

    [Fact]
    public async Task an_archived_stream_is_masked_under_archive_partitioning()
    {
        maskingEnabled(opts => opts.Events.UseArchivedStreamPartitioning = true);
        var streamId = await anArchivedStream();

        await theStore.Advanced.ApplyEventDataMasking(x => x.IncludeStream(streamId));

        await theRowWasMasked(streamId);
    }

    [Fact]
    public async Task only_the_named_tenants_archived_stream_is_masked()
    {
        maskingEnabled(opts => opts.Events.TenancyStyle = TenancyStyle.Conjoined);

        var streamId = Guid.NewGuid();

        foreach (var tenantId in new[] { "red", "blue" })
        {
            await using var session = theStore.LightweightSession(tenantId);
            session.Events.StartStream<Quest>(streamId, new QuestStarted { Name = Secret });
            await session.SaveChangesAsync();

            session.Events.ArchiveStream(streamId);
            await session.SaveChangesAsync();
        }

        await theStore.Advanced.ApplyEventDataMasking(x => x.ForTenant("red").IncludeStream(streamId));

        async Task<string> storedFor(string tenantId)
        {
            await using var session = theStore.LightweightSession(tenantId);
            var events = await session.Events.QueryAllRawEvents()
                .Where(e => e.MaybeArchived() && e.StreamId == streamId)
                .ToListAsync();

            return events.Single().Data.ShouldBeOfType<QuestStarted>().Name;
        }

        (await storedFor("red")).ShouldBe(Masked);
        (await storedFor("blue")).ShouldBe(Secret);
    }

    /// <summary>
    /// A skipped event is left out of the same read, by a filter the store only applies when skipping is on,
    /// and it is no more erased than an archived one.
    /// </summary>
    [Fact]
    public async Task a_skipped_event_on_a_stream_is_masked()
    {
        maskingEnabled(opts => opts.Events.EnableEventSkippingInProjectionsOrSubscriptions = true);

        var streamId = theSession.Events.StartStream<Quest>(new QuestStarted { Name = Secret }).Id;
        await theSession.SaveChangesAsync();

        var started = await theSession.Events.QueryAllRawEvents().Where(e => e.StreamId == streamId).ToListAsync();
        await theStore.Storage.Database.MarkEventsAsSkipped(started.Select(e => e.Sequence).ToArray());

        await theStore.Advanced.ApplyEventDataMasking(x => x.IncludeStream(streamId));

        await theRowWasMasked(streamId);
    }

    /// <summary>
    /// Guards the read rather than the bug: a stream read that lost its identity check would turn this into
    /// a pass that matches nothing and reports success.
    /// </summary>
    [Fact]
    public async Task naming_a_stream_with_the_wrong_identity_type_is_still_refused()
    {
        maskingEnabled(opts => opts.Events.StreamIdentity = StreamIdentity.AsString);

        await Should.ThrowAsync<InvalidOperationException>(() =>
            theStore.Advanced.ApplyEventDataMasking(x => x.IncludeStream(Guid.NewGuid())));
    }
}
