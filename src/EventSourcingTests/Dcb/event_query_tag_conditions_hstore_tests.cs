#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Tags;
using Marten.Events;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Dcb;

/// <summary>
/// The jasperfx#737 <see cref="EventQuery.TagConditions"/> filter under
/// <see cref="DcbStorageMode.HStore"/>. The shared EventQueryCompliance suite exercises the
/// default TagTables translation (a correlated tag-table subquery); this class pins the
/// hstore-containment branch of the same filter, which no shared suite reaches because the
/// storage mode is a Marten-only opt-in.
/// </summary>
[Collection("OneOffs")]
public class event_query_tag_conditions_hstore_tests: OneOffConfigurationsContext, IAsyncLifetime
{
    private void ConfigureStore()
    {
        StoreOptions(opts =>
        {
            opts.Events.AddEventType<StudentEnrolled>();
            opts.Events.AddEventType<AssignmentSubmitted>();

            opts.Events.DcbStorageMode = DcbStorageMode.HStore;

            opts.Events.RegisterTagType<StudentId>("student");
            opts.Events.RegisterTagType<CourseId>("course");
        });
    }

    public override ValueTask InitializeAsync()
    {
        ConfigureStore();
        return default;
    }

    public override ValueTask DisposeAsync() => base.DisposeAsync();

    private Task<PagedEvents> queryAsync(EventQuery query)
        => ((IReadOnlyEventStore)theSession.Events).QueryEventsAsync(query, CancellationToken.None);

    [Fact]
    public async Task tag_conditions_filter_by_hstore_containment()
    {
        var matching = new StudentId(Guid.NewGuid());
        var other = new StudentId(Guid.NewGuid());

        var tagged = theSession.Events.BuildEvent(new StudentEnrolled("Alice", "Math"));
        tagged.WithTag(matching);
        theSession.Events.Append(Guid.NewGuid(), tagged);

        var decoy = theSession.Events.BuildEvent(new StudentEnrolled("Bob", "Math"));
        decoy.WithTag(other);
        theSession.Events.Append(Guid.NewGuid(), decoy);

        // And one event carrying no tag at all, so "returned everything" cannot pass.
        theSession.Events.Append(Guid.NewGuid(), new StudentEnrolled("Carol", "Art"));
        await theSession.SaveChangesAsync();

        var result = await queryAsync(new EventQuery
        {
            TagConditions = EventTagQuerySpec.From(new EventTagQuery().Or(matching)),
            PageSize = 1000
        });

        result.TotalCount.ShouldBe(1);
        result.Events.Single().Data.ShouldBeOfType<StudentEnrolled>().StudentName.ShouldBe("Alice");
    }

    [Fact]
    public async Task a_condition_scoped_to_an_event_type_narrows_the_unscoped_match()
    {
        var student = new StudentId(Guid.NewGuid());

        var enrolled = theSession.Events.BuildEvent(new StudentEnrolled("Alice", "Math"));
        enrolled.WithTag(student);
        theSession.Events.Append(Guid.NewGuid(), enrolled);

        var submitted = theSession.Events.BuildEvent(new AssignmentSubmitted("HW1", 95));
        submitted.WithTag(student);
        theSession.Events.Append(Guid.NewGuid(), submitted);
        await theSession.SaveChangesAsync();

        var unscoped = await queryAsync(new EventQuery
        {
            TagConditions = EventTagQuerySpec.From(new EventTagQuery().Or(student)),
            PageSize = 1000
        });
        unscoped.TotalCount.ShouldBe(2);

        var scoped = await queryAsync(new EventQuery
        {
            TagConditions = EventTagQuerySpec.From(
                new EventTagQuery().Or<StudentEnrolled, StudentId>(student)),
            PageSize = 1000
        });

        scoped.TotalCount.ShouldBe(1);
        scoped.Events.Single().Data.ShouldBeOfType<StudentEnrolled>();
    }

    [Fact]
    public async Task tag_conditions_and_compose_with_the_other_filters()
    {
        var student = new StudentId(Guid.NewGuid());

        var first = theSession.Events.BuildEvent(new StudentEnrolled("Alice", "Math"));
        first.WithTag(student);
        theSession.Events.Append(Guid.NewGuid(), first);

        var second = theSession.Events.BuildEvent(new StudentEnrolled("Alice", "Art"));
        second.WithTag(student);
        theSession.Events.Append(Guid.NewGuid(), second);
        await theSession.SaveChangesAsync();

        var all = await queryAsync(new EventQuery
        {
            TagConditions = EventTagQuerySpec.From(new EventTagQuery().Or(student)),
            PageSize = 1000
        });
        all.TotalCount.ShouldBe(2);

        // The sequence window admits only the second tagged event — proving the tag selection
        // ANDs with the rest of the query rather than replacing it.
        var windowed = await queryAsync(new EventQuery
        {
            TagConditions = EventTagQuerySpec.From(new EventTagQuery().Or(student)),
            SequenceFloor = all.Events[^1].Sequence,
            PageSize = 1000
        });

        windowed.TotalCount.ShouldBe(1);
        windowed.Events.Single().Sequence.ShouldBe(all.Events[^1].Sequence);
    }
}
