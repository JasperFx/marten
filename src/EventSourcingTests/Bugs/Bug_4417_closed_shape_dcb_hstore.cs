#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using EventSourcingTests.Dcb;
using JasperFx.Events;
using JasperFx.Events.Tags;
using Marten;
using Marten.Events;
using Marten.Events.Dcb;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Bugs;

/// <summary>
/// Pins the closed-shape adapter as a working configuration for DCB
/// <see cref="DcbStorageMode.HStore"/> mode (#4417 suite-sweep follow-up).
/// </summary>
/// <remarks>
/// <para>
/// In HStore mode <c>mt_events</c> gains a <c>tags hstore</c> column, and
/// the <c>mt_quick_append_events</c> function signature is trimmed of the
/// per-tag <c>varchar[]</c> parameters — tags are written via a follow-up
/// <c>UPDATE</c> keyed on the event id (see <c>EventTagOperations</c> +
/// <c>QuickEventAppender</c>). The closed-shape adapter doesn't need to
/// do anything special for HStore — the per-event INSERT or function call
/// proceeds normally, the separate tag-update operation handles tags.
/// </para>
/// <para>
/// This test exercises that path explicitly so we don't regress it
/// silently: write events with tags in HStore mode under the closed-shape
/// flag, then read back via tag queries and confirm the tags survived
/// the round trip.
/// </para>
/// </remarks>
public class Bug_4417_closed_shape_dcb_hstore : OneOffConfigurationsContext
{
    [Fact]
    public async Task hstore_tags_round_trip_under_closed_shape_flag()
    {
        StoreOptions(opts =>
        {
            opts.Events.UseClosedShapeStorage = true;

            opts.Events.AddEventType<StudentEnrolled>();
            opts.Events.AddEventType<AssignmentSubmitted>();

            opts.Events.DcbStorageMode = DcbStorageMode.HStore;
            opts.Events.RegisterTagType<StudentId>("student");
            opts.Events.RegisterTagType<CourseId>("course");
        });

        var studentId = new StudentId(Guid.NewGuid());
        var courseId = new CourseId(Guid.NewGuid());
        var streamId = Guid.NewGuid();

        await using (var session = theStore.LightweightSession())
        {
            var enrolled = session.Events.BuildEvent(new StudentEnrolled("Alice", "Math"));
            enrolled.WithTag(studentId, courseId);
            session.Events.Append(streamId, enrolled);

            var assignment = session.Events.BuildEvent(new AssignmentSubmitted("HW1", 95));
            assignment.WithTag(studentId, courseId);
            session.Events.Append(streamId, assignment);

            await session.SaveChangesAsync();
        }

        // Query by tag — exercises the HStore @> containment operator.
        await using (var query = theStore.LightweightSession())
        {
            var byStudent = await query.Events.QueryByTagsAsync(
                new EventTagQuery().Or<StudentId>(studentId));
            byStudent.Count.ShouldBe(2);

            var byCourse = await query.Events.QueryByTagsAsync(
                new EventTagQuery().Or<CourseId>(courseId));
            byCourse.Count.ShouldBe(2);
        }

        // Fetch the stream the normal way and verify the events came back
        // intact through the closed-shape read path.
        await using (var query = theStore.LightweightSession())
        {
            var events = (await query.Events.FetchStreamAsync(streamId)).ToArray();
            events.Length.ShouldBe(2);
            events[0].Data.ShouldBeOfType<StudentEnrolled>().StudentName.ShouldBe("Alice");
            events[1].Data.ShouldBeOfType<AssignmentSubmitted>().AssignmentName.ShouldBe("HW1");
        }
    }

    [Fact]
    public async Task hstore_tags_round_trip_with_quick_append_mode()
    {
        // Explicitly exercise the AppendMode = Quick variant — the bulk
        // mt_quick_append_events call. (The default in v9 is
        // QuickWithServerTimestamps; we pin Quick here to cover both
        // code paths.)
        StoreOptions(opts =>
        {
            opts.Events.UseClosedShapeStorage = true;
            opts.Events.AppendMode = EventAppendMode.Quick;

            opts.Events.AddEventType<StudentEnrolled>();
            opts.Events.DcbStorageMode = DcbStorageMode.HStore;
            opts.Events.RegisterTagType<StudentId>("student");
        });

        var studentId = new StudentId(Guid.NewGuid());
        var streamId = Guid.NewGuid();

        await using (var session = theStore.LightweightSession())
        {
            var enrolled = session.Events.BuildEvent(new StudentEnrolled("Bob", "Physics"));
            enrolled.WithTag(studentId);
            session.Events.Append(streamId, enrolled);
            // Second event in the same SaveChanges to hit the multi-event
            // bulk path rather than the single-event QuickWithVersion path.
            var enrolled2 = session.Events.BuildEvent(new StudentEnrolled("Bob", "Physics 2"));
            enrolled2.WithTag(studentId);
            session.Events.Append(streamId, enrolled2);
            await session.SaveChangesAsync();
        }

        await using (var query = theStore.LightweightSession())
        {
            var byStudent = await query.Events.QueryByTagsAsync(
                new EventTagQuery().Or<StudentId>(studentId));
            byStudent.Count.ShouldBe(2);
        }
    }
}
