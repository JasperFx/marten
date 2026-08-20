#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Tags;
using Marten;
using Marten.Events;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Dcb;

/// <summary>
/// A store whose history arrived through <c>BulkInsertEventsAsync</c> — a migration from another system,
/// a restore — has to answer DCB tag queries about that history too. The bulk path writes events with a
/// single COPY into <c>mt_events</c> over a fixed column list, so a tag only survives it if that column
/// list carries it. In <see cref="DcbStorageMode.HStore" /> the tags are a column on the events table, so
/// they ride along in the same COPY.
/// <para>
/// Without that, a bulk-imported event is invisible to every tag query, and invisible is exactly what a
/// consistency boundary must not be: the answer looks like "no events" rather than like a failure.
/// </para>
/// </summary>
[Collection("OneOffs")]
public class hstore_dcb_tags_survive_a_bulk_import: OneOffConfigurationsContext, IAsyncLifetime
{
    public override ValueTask InitializeAsync()
    {
        StoreOptions(opts =>
        {
            opts.Events.AddEventType<StudentEnrolled>();
            opts.Events.AddEventType<AssignmentSubmitted>();

            opts.Events.DcbStorageMode = DcbStorageMode.HStore;
            opts.Events.RegisterTagType<StudentId>("student");
            opts.Events.RegisterTagType<CourseId>("course");
        });

        return default;
    }

    public override ValueTask DisposeAsync() => base.DisposeAsync();

    [Fact]
    public async Task a_tagged_event_is_found_after_a_bulk_import()
    {
        var studentId = new StudentId(Guid.NewGuid());
        var courseId = new CourseId(Guid.NewGuid());

        var action = StreamAction.Start(theStore.Events, Guid.NewGuid(),
            new StudentEnrolled("Alice", "Math"));
        foreach (var e in action.Events)
        {
            e.WithTag(studentId, courseId);
        }

        await theStore.BulkInsertEventsAsync(new List<StreamAction> { action });

        var byStudent = await theSession.Events.QueryByTagsAsync(new EventTagQuery().Or<StudentId>(studentId));
        byStudent.Count.ShouldBe(1);
        byStudent[0].Data.ShouldBeOfType<StudentEnrolled>().StudentName.ShouldBe("Alice");

        // Both registered tag types land in the one hstore, so either finds it.
        var byCourse = await theSession.Events.QueryByTagsAsync(new EventTagQuery().Or<CourseId>(courseId));
        byCourse.Count.ShouldBe(1);
    }

    [Fact]
    public async Task an_untagged_event_in_the_same_import_is_not_found()
    {
        var studentId = new StudentId(Guid.NewGuid());

        var tagged = StreamAction.Start(theStore.Events, Guid.NewGuid(), new StudentEnrolled("Alice", "Math"));
        foreach (var e in tagged.Events)
        {
            e.WithTag(studentId);
        }

        // No tag at all: the column has to be written as null rather than skipped, or the COPY row goes out
        // of step with its column list.
        var untagged = StreamAction.Start(theStore.Events, Guid.NewGuid(), new StudentEnrolled("Bob", "Math"));

        await theStore.BulkInsertEventsAsync(new List<StreamAction> { tagged, untagged });

        var found = await theSession.Events.QueryByTagsAsync(new EventTagQuery().Or<StudentId>(studentId));
        found.Count.ShouldBe(1);
        found[0].Data.ShouldBeOfType<StudentEnrolled>().StudentName.ShouldBe("Alice");

        // And both events did land — the import is not what dropped Bob.
        var all = await theSession.Events.QueryAllRawEvents().ToListAsync(default);
        all.Count.ShouldBe(2);
    }
}
