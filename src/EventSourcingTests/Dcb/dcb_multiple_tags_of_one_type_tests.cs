#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Tags;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Dcb;

public record HomeWorkCopied(string AssignmentName, StudentId CopiedFrom, StudentId CopiedBy);

// A homework-copied event names two students, so it carries two tags of the student tag type and
// has to be findable from either of them. StartStream stores both rows; Events.Append stores only
// the first, on the store's default append mode.
[Collection("OneOffs")]
public class dcb_multiple_tags_of_one_type_tests: OneOffConfigurationsContext
{
    private void ConfigureStore()
    {
        StoreOptions(opts =>
        {
            opts.Events.AddEventType<HomeWorkCopied>();
            opts.Events.AddEventType<StudentEnrolled>();

            opts.Events.RegisterTagType<StudentId>("student");
        });
    }

    [Fact]
    public async Task append_stores_every_tag_of_one_type()
    {
        ConfigureStore();
        var streamId = Guid.NewGuid();
        var copiedFrom = new StudentId(Guid.NewGuid());
        var copiedBy = new StudentId(Guid.NewGuid());
        theSession.Events.StartStream(streamId, new StudentEnrolled("Alice", "Math"));
        await theSession.SaveChangesAsync();
        var evt = theSession.Events.BuildEvent(new HomeWorkCopied("HW1", copiedFrom, copiedBy));
        evt.WithTag(copiedFrom);
        evt.WithTag(copiedBy);

        theSession.Events.Append(streamId, evt);
        await theSession.SaveChangesAsync();

        (await GetEventsWithTag(copiedFrom)).ShouldHaveSingleItem();
        (await GetEventsWithTag(copiedBy)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task start_stream_stores_every_tag_of_one_type()
    {
        ConfigureStore();
        var streamId = Guid.NewGuid();
        var copiedFrom = new StudentId(Guid.NewGuid());
        var copiedBy = new StudentId(Guid.NewGuid());
        var evt = theSession.Events.BuildEvent(new HomeWorkCopied("HW1", copiedFrom, copiedBy));
        evt.WithTag(copiedFrom);
        evt.WithTag(copiedBy);

        theSession.Events.StartStream(streamId, evt);
        await theSession.SaveChangesAsync();

        (await GetEventsWithTag(copiedFrom)).ShouldHaveSingleItem();
        (await GetEventsWithTag(copiedBy)).ShouldHaveSingleItem();
    }

    private async Task<IReadOnlyList<IEvent>> GetEventsWithTag(StudentId tagValue)
    {
        await using var query = theStore.LightweightSession();
        var eventsWithTag = await query.Events.QueryByTagsAsync(new EventTagQuery().Or<StudentId>(tagValue));
        return eventsWithTag;
    }
}
