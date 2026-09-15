using System;
using System.Linq;
using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using JasperFx.Events;
using JasperFx.Events.Protected;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Bugs;

/// <summary>
/// #5379, event side: replacing an event (which stream compaction does for the surviving event)
/// used to re-stamp <c>mt_events.timestamp</c> with <c>now() at time zone 'utc'</c>. Same defect
/// as the document metadata column: a naive UTC wall-clock value assigned to a <c>timestamptz</c>
/// is re-interpreted in the session's <c>TimeZone</c>, so on a non-UTC database the compacted
/// snapshot carried a timestamp off by the UTC offset.
/// </summary>
/// <remarks>
/// CI runs Postgres at <c>Etc/UTC</c>, where the bug is invisible, so the session time zone is set
/// on the connection string. Both directions are covered.
/// </remarks>
public class Bug_5379_replaced_event_timestamp_on_non_utc_session: OneOffConfigurationsContext
{
    [Theory]
    [InlineData("Europe/Berlin")]
    [InlineData("America/Chicago")]
    public async Task compacting_a_stream_stamps_the_snapshot_with_the_actual_instant(string timeZone)
    {
        StoreOptions(opts =>
        {
            var connectionString = new NpgsqlConnectionStringBuilder(ConnectionSource.ConnectionString)
            {
                Timezone = timeZone
            }.ConnectionString;

            opts.Connection(connectionString);
        });

        var streamId = Guid.NewGuid();
        theSession.Events.StartStream<Letters>(streamId, new AEvent(), new BEvent(), new CEvent());
        await theSession.SaveChangesAsync();

        // A floor for the replacement: the snapshot is written strictly after these were appended
        var appended = (await theSession.Events.FetchStreamAsync(streamId)).Max(x => x.Timestamp);

        await theSession.Events.CompactStreamAsync<Letters>(streamId);
        await theSession.SaveChangesAsync();
        var compactedBy = DateTimeOffset.UtcNow.AddSeconds(5);

        var compacted = (await theSession.Events.FetchStreamAsync(streamId)).Single();
        compacted.Data.ShouldBeOfType<Compacted<Letters>>();

        compacted.Timestamp.ShouldBeGreaterThanOrEqualTo(appended);
        compacted.Timestamp.ShouldBeLessThanOrEqualTo(compactedBy);
    }
}
