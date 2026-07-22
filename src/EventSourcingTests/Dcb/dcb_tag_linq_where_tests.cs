#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Tags;
using Marten;
using Marten.Events;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Dcb;

// Reuses the StudentId / CourseId tag types and the StudentEnrolled / AssignmentSubmitted events
// declared in dcb_tag_query_and_consistency_tests.cs (same namespace).
[Collection("OneOffs")]
public class dcb_tag_linq_where_tests: OneOffConfigurationsContext
{
    private void ConfigureStore(DcbStorageMode mode = DcbStorageMode.TagTables)
    {
        StoreOptions(opts =>
        {
            opts.Events.DcbStorageMode = mode;

            opts.Events.AddEventType<StudentEnrolled>();
            opts.Events.AddEventType<AssignmentSubmitted>();

            opts.Events.RegisterTagType<StudentId>("student");
            opts.Events.RegisterTagType<CourseId>("course");
        });
    }

    private async Task AppendTagged(Guid streamId, object eventData, params object[] tags)
    {
        var wrapped = theSession.Events.BuildEvent(eventData);
        wrapped.WithTag(tags);
        theSession.Events.Append(streamId, wrapped);
        await theSession.SaveChangesAsync();
    }

    [Fact]
    public async Task has_tag_matches_only_events_carrying_that_tag_value()
    {
        ConfigureStore();

        var alice = new StudentId(Guid.NewGuid());
        var bob = new StudentId(Guid.NewGuid());

        await AppendTagged(Guid.NewGuid(), new StudentEnrolled("Alice", "Math"), alice);
        await AppendTagged(Guid.NewGuid(), new StudentEnrolled("Bob", "Math"), bob);

        var events = await theSession.Events.QueryAllRawEvents()
            .Where(e => e.HasTag<StudentId>(alice))
            .ToListAsync();

        events.Count.ShouldBe(1);
        events.Single().Data.ShouldBeOfType<StudentEnrolled>().StudentName.ShouldBe("Alice");
    }

    [Fact]
    public async Task has_tag_composes_with_a_normal_event_predicate_in_one_where()
    {
        ConfigureStore();

        var alice = new StudentId(Guid.NewGuid());
        var streamId = Guid.NewGuid();

        // Two events for Alice, both tagged with her StudentId, but of different event types.
        await AppendTagged(streamId, new StudentEnrolled("Alice", "Math"), alice);
        await AppendTagged(streamId, new AssignmentSubmitted("HW1", 95), alice);

        // "Alice's events, but only the enrollments" — the tag predicate AND a normal event predicate.
        var events = await theSession.Events.QueryAllRawEvents()
            .Where(e => e.HasTag<StudentId>(alice) && e.EventTypesAre(typeof(StudentEnrolled)))
            .ToListAsync();

        events.Count.ShouldBe(1);
        events.Single().Data.ShouldBeOfType<StudentEnrolled>();
    }

    [Fact]
    public async Task has_tag_composes_with_a_timestamp_predicate()
    {
        ConfigureStore();

        var alice = new StudentId(Guid.NewGuid());
        await AppendTagged(Guid.NewGuid(), new StudentEnrolled("Alice", "Math"), alice);

        var cutoff = DateTimeOffset.UtcNow.AddDays(-1);

        var events = await theSession.Events.QueryAllRawEvents()
            .Where(e => e.HasTag<StudentId>(alice) && e.Timestamp > cutoff)
            .ToListAsync();

        events.Count.ShouldBe(1);
    }

    [Fact]
    public async Task and_of_two_tag_predicates_requires_both_tags()
    {
        ConfigureStore();

        var alice = new StudentId(Guid.NewGuid());
        var math = new CourseId(Guid.NewGuid());

        // Event 1 carries BOTH tags; event 2 carries only the student tag.
        await AppendTagged(Guid.NewGuid(), new StudentEnrolled("Alice", "Math"), alice, math);
        await AppendTagged(Guid.NewGuid(), new StudentEnrolled("Alice", "Science"), alice);

        var events = await theSession.Events.QueryAllRawEvents()
            .Where(e => e.HasTag<StudentId>(alice) && e.HasTag<CourseId>(math))
            .ToListAsync();

        // Only the event tagged with both survives the AND.
        events.Count.ShouldBe(1);
        events.Single().Data.ShouldBeOfType<StudentEnrolled>().CourseName.ShouldBe("Math");
    }

    [Fact]
    public async Task has_tag_works_in_hstore_storage_mode()
    {
        ConfigureStore(DcbStorageMode.HStore);

        var alice = new StudentId(Guid.NewGuid());
        var bob = new StudentId(Guid.NewGuid());
        var math = new CourseId(Guid.NewGuid());

        await AppendTagged(Guid.NewGuid(), new StudentEnrolled("Alice", "Math"), alice, math);
        await AppendTagged(Guid.NewGuid(), new StudentEnrolled("Bob", "Math"), bob);

        // Single tag against the hstore column.
        var byStudent = await theSession.Events.QueryAllRawEvents()
            .Where(e => e.HasTag<StudentId>(alice))
            .ToListAsync();
        byStudent.Count.ShouldBe(1);
        byStudent.Single().Data.ShouldBeOfType<StudentEnrolled>().StudentName.ShouldBe("Alice");

        // AND of two tags, both resolved against the same hstore column.
        var bothTags = await theSession.Events.QueryAllRawEvents()
            .Where(e => e.HasTag<StudentId>(alice) && e.HasTag<CourseId>(math))
            .ToListAsync();
        bothTags.Count.ShouldBe(1);
        bothTags.Single().Data.ShouldBeOfType<StudentEnrolled>().StudentName.ShouldBe("Alice");
    }

    [Fact]
    public async Task has_tag_for_an_unregistered_tag_type_throws()
    {
        ConfigureStore();

        var unknown = new UnregisteredTag(Guid.NewGuid());

        await Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            await theSession.Events.QueryAllRawEvents()
                .Where(e => e.HasTag<UnregisteredTag>(unknown))
                .ToListAsync();
        });
    }

    public record UnregisteredTag(Guid Value);
}
