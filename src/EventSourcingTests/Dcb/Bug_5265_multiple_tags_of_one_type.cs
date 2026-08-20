#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Tags;
using Marten.Events;
using Marten.Exceptions;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Dcb;

public record TeamId(Guid Value);

public record MatchPlayed(TeamId Home, TeamId Away, string Venue);

/// <summary>
/// #5265, beyond the reported two-tag case. The bulk <c>mt_quick_append_events</c> function carries
/// one <c>varchar[]</c> per registered tag type, parallel to the events array, so it has exactly one
/// slot per (event, tag type). Everything that routes through that function — which is every append
/// except a non-partitioned <c>StartStream</c> — could therefore only ever store one tag of a type.
/// </summary>
[Collection("OneOffs")]
public class Bug_5265_multiple_tags_of_one_type: OneOffConfigurationsContext
{
    private void ConfigureStore(DcbStorageMode mode = DcbStorageMode.TagTables)
    {
        StoreOptions(opts =>
        {
            opts.Events.DcbStorageMode = mode;
            opts.Events.AddEventType<MatchPlayed>();
            opts.Events.AddEventType<StudentEnrolled>();

            opts.Events.RegisterTagType<TeamId>("team");
            opts.Events.RegisterTagType<CourseId>("course");
        });
    }

    private async Task<IReadOnlyList<IEvent>> EventsTaggedWith(TeamId team)
    {
        await using var query = theStore.LightweightSession();
        return await query.Events.QueryByTagsAsync(new EventTagQuery().Or<TeamId>(team));
    }

    [Fact]
    public async Task append_stores_three_tags_of_one_type_alongside_another_type()
    {
        ConfigureStore();

        var streamId = Guid.NewGuid();
        theSession.Events.StartStream(streamId, new StudentEnrolled("Alice", "Math"));
        await theSession.SaveChangesAsync();

        // Three of one type plus one of another, to pin down that "skip the first of each type"
        // is per type rather than per event — an off-by-one here silently drops a tag.
        var home = new TeamId(Guid.NewGuid());
        var away = new TeamId(Guid.NewGuid());
        var neutral = new TeamId(Guid.NewGuid());
        var course = new CourseId(Guid.NewGuid());

        var evt = theSession.Events.BuildEvent(new MatchPlayed(home, away, "Neutral ground"));
        evt.WithTag(home);
        evt.WithTag(away);
        evt.WithTag(neutral);
        evt.WithTag(course);

        theSession.Events.Append(streamId, evt);
        await theSession.SaveChangesAsync();

        foreach (var team in new[] { home, away, neutral })
        {
            (await EventsTaggedWith(team)).ShouldHaveSingleItem();
        }

        await using var query = theStore.LightweightSession();
        (await query.Events.QueryByTagsAsync(new EventTagQuery().Or<CourseId>(course)))
            .ShouldHaveSingleItem();
    }

    [Fact]
    public async Task the_same_tag_value_twice_on_one_event_stores_one_row()
    {
        ConfigureStore();

        var streamId = Guid.NewGuid();
        theSession.Events.StartStream(streamId, new StudentEnrolled("Bob", "Math"));
        await theSession.SaveChangesAsync();

        // The tag tables' PK is (value, [tenant_id], seq_id), so a repeated value is the one case
        // the surplus insert's `on conflict do nothing` actually catches.
        var team = new TeamId(Guid.NewGuid());
        var evt = theSession.Events.BuildEvent(new MatchPlayed(team, team, "Home"));
        evt.WithTag(team);
        evt.WithTag(team);

        theSession.Events.Append(streamId, evt);
        await Should.NotThrowAsync(() => theSession.SaveChangesAsync());

        (await EventsTaggedWith(team)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task multiple_events_in_one_append_each_keep_their_tags()
    {
        ConfigureStore();

        var streamId = Guid.NewGuid();
        theSession.Events.StartStream(streamId, new StudentEnrolled("Cara", "Math"));
        await theSession.SaveChangesAsync();

        var first = (home: new TeamId(Guid.NewGuid()), away: new TeamId(Guid.NewGuid()));
        var second = (home: new TeamId(Guid.NewGuid()), away: new TeamId(Guid.NewGuid()));

        var one = theSession.Events.BuildEvent(new MatchPlayed(first.home, first.away, "One"));
        one.WithTag(first.home);
        one.WithTag(first.away);

        var two = theSession.Events.BuildEvent(new MatchPlayed(second.home, second.away, "Two"));
        two.WithTag(second.home);
        two.WithTag(second.away);

        theSession.Events.Append(streamId, one, two);
        await theSession.SaveChangesAsync();

        foreach (var team in new[] { first.home, first.away, second.home, second.away })
        {
            (await EventsTaggedWith(team)).ShouldHaveSingleItem();
        }
    }

    [Fact]
    public async Task hstore_mode_refuses_two_tags_of_one_type_rather_than_dropping_one()
    {
        ConfigureStore(DcbStorageMode.HStore);

        var streamId = Guid.NewGuid();
        theSession.Events.StartStream(streamId, new StudentEnrolled("Dan", "Math"));
        await theSession.SaveChangesAsync();

        // hstore maps one key to one value and the key here is the tag type, so this mode cannot
        // represent the case at all. Dropping the surplus silently is the one outcome a consistency
        // boundary must not have — an absent tag is not an error to a DCB query, just an absence.
        var home = new TeamId(Guid.NewGuid());
        var away = new TeamId(Guid.NewGuid());

        var evt = theSession.Events.BuildEvent(new MatchPlayed(home, away, "Neutral ground"));
        evt.WithTag(home);
        evt.WithTag(away);

        theSession.Events.Append(streamId, evt);

        var ex = await Should.ThrowAsync<MartenNotSupportedException>(() => theSession.SaveChangesAsync());
        ex.Message.ShouldContain("TeamId");
        ex.Message.ShouldContain("TagTables");
    }
}
