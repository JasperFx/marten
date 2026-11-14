#nullable enable
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Tags;
using Marten;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Dcb;

public record SeatingClassId(Guid Value);
public record WaitlistClassId(Guid Value);

public record ClassSeatTaken(Guid MemberId);
public record ClassWaitlistJoined(Guid MemberId);

[BoundaryAggregate]
public class ClassSeating
{
    public int SeatsTaken { get; set; }

    public void Apply(ClassSeatTaken _) => SeatsTaken++;
}

// #5887: the DCB append condition fails only when an event MATCHING THE QUERY was appended after
// the read. Marten's boundary save used to bump the mt_dcb_tag_version row of every tag it read,
// so two boundaries reading the same tag value conflicted even when neither one appended an event
// carrying that tag. The check now locks and compares the rows it read and leaves the versions
// alone; only the producer-side bump of the appended events' own tags moves a version.
//
// The store-agnostic facts that used to live here run for every Critter Stack store in
// JasperFx.Events.ComplianceTests' DcbAppendConditionCompliance (dcb_append_condition_compliance).
// What stays is Marten's own: the mt_dcb_tag_version rows a check does not move.
[Collection("OneOffs")]
public class Bug_5887_boundary_check_does_not_bump_what_it_reads: OneOffConfigurationsContext
{
    private void ConfigureStore()
    {
        StoreOptions(opts =>
        {
            opts.Events.AddEventType<ClassSeatTaken>();
            opts.Events.AddEventType<ClassWaitlistJoined>();

            opts.Events.RegisterTagType<SeatingClassId>("seating_class");
            opts.Events.RegisterTagType<WaitlistClassId>("waitlist_class");

            // No write retries: a retried deadlock (40P01) commits after a one-second
            // deadlock_timeout and passes, so with retries on these tests cannot tell a save that
            // waited its turn from one that deadlocked.
            opts.ConfigureWritePolly(_ => { });
        });
    }

    private static EventTagQuery SeatingQuery(Guid classId)
        => new EventTagQuery().Or(new SeatingClassId(classId));

    // The appended event carries only the waitlist tag, which the seating query does not read
    private static IEvent JoinWaitlist(IDocumentSession session, Guid classId)
    {
        var joined = session.Events.BuildEvent(new ClassWaitlistJoined(Guid.NewGuid()));
        joined.WithTag(new WaitlistClassId(classId));
        return joined;
    }

    private static IEvent TakeSeat(IDocumentSession session, Guid classId)
    {
        var taken = session.Events.BuildEvent(new ClassSeatTaken(Guid.NewGuid()));
        taken.WithTag(new SeatingClassId(classId));
        return taken;
    }

    private async Task SeedSeatAsync(Guid classId)
    {
        await using var session = theStore.LightweightSession();
        var boundary = await session.Events.FetchForWritingByTags<ClassSeating>(SeatingQuery(classId));
        boundary.AppendOne(TakeSeat(session, classId));
        await session.SaveChangesAsync();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task the_check_leaves_the_version_of_a_row_it_only_read_alone(bool rowExists)
    {
        ConfigureStore();
        var classId = Guid.NewGuid();
        if (rowExists) await SeedSeatAsync(classId);

        var before = await TagVersionAsync("seating_class", classId) ?? 0;

        await using var session = theStore.LightweightSession();
        var boundary = await session.Events.FetchForWritingByTags<ClassSeating>(SeatingQuery(classId));
        boundary.AppendOne(JoinWaitlist(session, classId));
        await session.SaveChangesAsync();

        ((await TagVersionAsync("seating_class", classId)) ?? 0).ShouldBe(before);

        // ...and the tag the appended event does carry still moves
        (await TagVersionAsync("waitlist_class", classId)).ShouldBe(1);
    }

    private async Task<long?> TagVersionAsync(string tagTable, Guid value)
    {
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            $"select version from {SchemaName}.mt_dcb_tag_version where tag_table = @table and tag_value = @value";
        cmd.Parameters.AddWithValue("table", tagTable);
        cmd.Parameters.AddWithValue("value", value.ToString());

        var raw = await cmd.ExecuteScalarAsync();
        return raw == null || raw == DBNull.Value ? null : Convert.ToInt64(raw);
    }
}
