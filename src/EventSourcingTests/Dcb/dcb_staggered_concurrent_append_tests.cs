#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Tags;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Dcb;

// Concurrent appends guarded by the same DCB tag boundary must serialize: when several
// writers race the same boundary, only one may commit. This pins that invariant under
// staggered (non-lockstep) concurrency, exercised with many racers over many rounds.
// Note: #4591's test only covers the lockstep case (racers synchronized).
//
// #5300: what makes the staggered case different is that FetchForWritingByTags reads the
// events and the mt_dcb_tag_version row in two SEPARATE statements, and at READ COMMITTED
// each statement takes its own snapshot -- so a commit landing between them is visible to
// one read and not the other. Pre-fix the version was captured SECOND, which paired a fresh
// version with a stale aggregate and let the save's `where version = $captured` match a check
// the caller never actually made. The window is the whole duration of the events query, which
// is why a handful of concurrent requests is enough to hit it. #4591's barrier syncs every
// racer AFTER the fetch, so all of them capture the same version and the ordering bug cannot
// show up there.
//
// The fix captures the version FIRST. If anything commits mid-fetch the captured version is
// stale-low and the assertion fails -- a spurious conflict the caller retries, never a lost
// one. Do not "tidy" the two statements back into the other order.
[Collection("OneOffs")]
public class dcb_staggered_concurrent_append_tests : OneOffConfigurationsContext
{
    private const int Racers = 24;
    private const int Rounds = 200;

    public dcb_staggered_concurrent_append_tests()
    {
        StoreOptions(opts =>
        {
            opts.Events.AddEventType<Enrolled>();
            opts.Events.AddEventType<ProgressRecorded>();

            opts.Events.RegisterTagType<EnrolleeId>("enrollee").ForAggregate<SubscriptionState>();
            opts.Events.RegisterTagType<ProgramId>("program").ForAggregate<SubscriptionState>();
        });
    }

    [Fact]
    public async Task first_appends_to_a_shared_tag_serialize_to_one_winner()
    {
        long appended = 0;
        for (var round = 0; round < Rounds; round++)
        {
            var programId = new ProgramId(Guid.NewGuid());
            await RaceToEnrollAsync(programId);

            appended = Math.Max(appended, await EnrollmentCountAsync(programId));
        }

        appended.ShouldBe(1);
    }

    // Same race, but the tag already has a version row (advanced by an unrelated
    // ProgressRecorded), so this exercises the UPDATE-WHERE-version path, not the
    // first-time INSERT.
    [Fact]
    public async Task appends_at_an_existing_version_serialize_to_one_winner()
    {
        long appended = 0;

        for (var round = 0; round < Rounds; round++)
        {
            var programId = new ProgramId(Guid.NewGuid());
            await SeedProgressAsync(programId);
            await RaceToEnrollAsync(programId);

            appended = Math.Max(appended, await EnrollmentCountAsync(programId));
        }

        appended.ShouldBe(1);
    }

    private Task RaceToEnrollAsync(ProgramId programId)
        => Task.WhenAll(Enumerable.Range(0, Racers).Select(_ => TryEnrollAsync(programId)));

    // Invariant: at most one enrollment per program. A racer that fetched after another
    // committed sees the enrollment and backs off.
    private async Task TryEnrollAsync(ProgramId programId)
    {
        await using var session = theStore.LightweightSession();
        var boundary = await session.Events.FetchForWritingByTags<SubscriptionState>(
            new EventTagQuery().Or<ProgramId>(programId));

        if (boundary.Aggregate is { EnrollmentCount: > 0 })
        {
            return;
        }

        var enrolled = session.Events.BuildEvent(new Enrolled("Student"));
        enrolled.WithTag(new EnrolleeId(Guid.NewGuid()), programId);
        boundary.AppendOne(enrolled);

        try
        {
            await session.SaveChangesAsync();
        }
        catch (DcbConcurrencyException)
        {
        }
    }

    private async Task SeedProgressAsync(ProgramId programId)
    {
        await using var session = theStore.LightweightSession();
        var boundary = await session.Events.FetchForWritingByTags<SubscriptionState>(
            new EventTagQuery().Or<ProgramId>(programId));
        var progress = session.Events.BuildEvent(new ProgressRecorded("kickoff"));
        progress.WithTag(new EnrolleeId(Guid.NewGuid()), programId);
        boundary.AppendOne(progress);
        await session.SaveChangesAsync();
    }

    private async Task<long> EnrollmentCountAsync(ProgramId programId)
    {
        await using var session = theStore.LightweightSession();
        var enrolled = await session.Events.QueryByTagsAsync(
            new EventTagQuery().Or<Enrolled, ProgramId>(programId));
        return enrolled.Count;
    }
}
