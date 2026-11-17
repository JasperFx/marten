using System;
using System.Linq;
using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using JasperFx.Events;
using Marten;
using Marten.Events.Archiving;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace EventSourcingTests.Rewriting;

public class rewriting_archived_compacted_and_tombstone_rows: OneOffConfigurationsContext
{
    public record ParcelSentV1(string To);

    public record ParcelSent(string Recipient);

    private async Task<long> countAsync(string where)
    {
        await using var conn = theStore.Storage.Database.CreateConnection();
        await conn.OpenAsync();
        return Convert.ToInt64(await conn
            .CreateCommand($"select count(*) from {SchemaName}.mt_events where {where}").ExecuteScalarAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task every_row_of_the_store_reads_back_as_it_was_and_archived_history_is_rewritten_too(
        bool archivedPartitioning)
    {
        StoreOptions(opts =>
        {
            opts.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;
            opts.Events.UseArchivedStreamPartitioning = archivedPartitioning;
            opts.Events.Upcast<ParcelSentV1, ParcelSent>(x => new ParcelSent(x.To));
            opts.Events.AddEventType<AEvent>();
            opts.Events.AddEventType<BEvent>();
        });

        // An archived stream that still has rows an upcaster reads
        var archived = Guid.NewGuid();
        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream(archived, new ParcelSentV1("Utrecht"));
            await session.SaveChangesAsync();
            session.Events.ArchiveStream(archived);
            await session.SaveChangesAsync();
        }

        // A compacted stream
        var compacted = Guid.NewGuid();
        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream<Letters>(compacted, new AEvent(), new BEvent(), new AEvent());
            await session.SaveChangesAsync();
            await session.Events.CompactStreamAsync<Letters>(compacted);
            await session.SaveChangesAsync();
        }

        // Tombstones: a start whose batch fails after its events drew their sequence numbers
        var target = Target.Random();
        await using (var session = theStore.LightweightSession())
        {
            session.Insert(target);
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream<Letters>(Guid.NewGuid(), new AEvent(), new BEvent());
            session.Insert(target);
            (await Record.ExceptionAsync(() => session.SaveChangesAsync())).ShouldNotBeNull();
        }

        await theStore.Options.EventGraph.WaitForPendingTombstonesAsync();
        (await countAsync("type = 'tombstone'")).ShouldBeGreaterThan(0);
        (await countAsync("is_archived")).ShouldBeGreaterThan(0);

        var result = await theStore.Advanced.RewriteStoredEventsAsync(x =>
        {
            x.IncludeAllTypes();
            x.BatchSize = 2;
        });

        var database = result.Databases.Single();
        database.UnknownTypes.ShouldBe(0);
        database.Skipped.ShouldBe(0);
        database.Rewritten.ShouldBe(1); // the archived ParcelSentV1, nothing else
        database.Visited.ShouldBe(await countAsync("true"));

        (await countAsync("type = 'parcel_sent_v1'")).ShouldBe(0);

        await using var query = theStore.QuerySession();
        var archivedEvents = await query.Events.QueryAllRawEvents()
            .Where(x => x.MaybeArchived() && x.StreamId == archived)
            .ToListAsync();
        archivedEvents.Select(x => x.Data).OfType<ParcelSent>().Single().ShouldBe(new ParcelSent("Utrecht"));

        var letters = await query.Events.AggregateStreamAsync<Letters>(compacted);
        letters!.ACount.ShouldBe(2);
        letters.BCount.ShouldBe(1);

        // A second run finds nothing to do
        var again = await theStore.Advanced.RewriteStoredEventsAsync(x => x.IncludeAllTypes());
        again.Databases.Single().Rewritten.ShouldBe(0);
    }
}
