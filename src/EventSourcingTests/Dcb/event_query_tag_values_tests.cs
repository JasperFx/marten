#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Descriptors;
using JasperFx.Events;
using Marten;
using Marten.Events;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Dcb;

/// <summary>
/// The two halves of #5365 / jasperfx#801 that no shared suite reaches.
///
/// <para>
/// <see cref="EventQuery.TagValues"/> itself is covered by <c>EventQueryCompliance</c>, but only in
/// the default <see cref="DcbStorageMode.TagTables"/> translation — the hstore branch is a
/// Marten-only opt-in, so its own translation of the same filter is pinned here alongside the
/// existing <c>event_query_tag_conditions_hstore_tests</c>.
/// </para>
///
/// <para>
/// The dictionary <c>IEventStore.QueryByTagsAsync</c> overload has no compliance coverage at all,
/// which is how it drifted from Polecat's: it matched the CLR type name only and compared the value
/// case-sensitively. That is what made the divergence undiscoverable — a caller holding a store
/// descriptor cannot tell which spelling an engine takes, and gets an <c>ArgumentException</c> at
/// runtime for guessing wrong. Both rules now come from the shared
/// <c>TagTypeRegistrationExtensions</c> matcher, and these facts hold that path to them.
/// </para>
/// </summary>
[Collection("OneOffs")]
public class event_query_tag_values_tests: OneOffConfigurationsContext
{
    private void ConfigureStore(DcbStorageMode mode)
    {
        StoreOptions(opts =>
        {
            opts.Events.AddEventType<StudentEnrolled>();
            opts.Events.AddEventType<AssignmentSubmitted>();

            opts.Events.DcbStorageMode = mode;

            opts.Events.RegisterTagType<StudentId>("student");
            opts.Events.RegisterTagType<CourseId>("course");
        });
    }

    private Task<PagedEvents> queryAsync(EventQuery query)
        => ((IReadOnlyEventStore)theSession.Events).QueryEventsAsync(query, CancellationToken.None);

    /// <summary>
    /// Two tagged events, a decoy at a different value of the same tag type, and an untagged event.
    /// Both decoys survive a store that drops the filter.
    /// </summary>
    private async Task<StudentId> seedAsync()
    {
        var matching = new StudentId(Guid.NewGuid());
        var other = new StudentId(Guid.NewGuid());

        var first = theSession.Events.BuildEvent(new StudentEnrolled("Alice", "Math"));
        first.WithTag(matching);
        theSession.Events.Append(Guid.NewGuid(), first);

        var second = theSession.Events.BuildEvent(new AssignmentSubmitted("HW1", 90));
        second.WithTag(matching);
        theSession.Events.Append(Guid.NewGuid(), second);

        var decoy = theSession.Events.BuildEvent(new StudentEnrolled("Bob", "Math"));
        decoy.WithTag(other);
        theSession.Events.Append(Guid.NewGuid(), decoy);

        theSession.Events.Append(Guid.NewGuid(), new StudentEnrolled("Carol", "Art"));
        await theSession.SaveChangesAsync();

        return matching;
    }

    [Theory]
    [InlineData(DcbStorageMode.TagTables)]
    [InlineData(DcbStorageMode.HStore)]
    public async Task tag_values_filter_in_both_storage_modes(DcbStorageMode mode)
    {
        ConfigureStore(mode);
        var matching = await seedAsync();

        var result = await queryAsync(new EventQuery
        {
            TagValues = { ["student"] = matching.Value.ToString() }, PageSize = 1000
        });

        result.TotalCount.ShouldBe(2);
        result.Events.ShouldContain(x => x.Data is StudentEnrolled);
        result.Events.ShouldContain(x => x.Data is AssignmentSubmitted);
    }

    /// <summary>
    /// A Guid tag rendered through <c>value::text</c> comes back lowercase from Postgres, so an
    /// operator pasting the uppercase form an ASP.NET route or another store handed them still finds
    /// their events. Exercised in both modes because the two translations lower different things.
    /// </summary>
    [Theory]
    [InlineData(DcbStorageMode.TagTables)]
    [InlineData(DcbStorageMode.HStore)]
    public async Task tag_values_match_a_value_regardless_of_casing(DcbStorageMode mode)
    {
        ConfigureStore(mode);
        var matching = await seedAsync();

        var upper = await queryAsync(new EventQuery
        {
            TagValues = { ["student"] = matching.Value.ToString().ToUpperInvariant() }, PageSize = 1000
        });

        upper.TotalCount.ShouldBe(2);
    }

    /// <summary>
    /// Entries AND, so an event carrying only one of the two tags is excluded — and the one carrying
    /// both is returned once, not once per matching tag.
    /// </summary>
    [Theory]
    [InlineData(DcbStorageMode.TagTables)]
    [InlineData(DcbStorageMode.HStore)]
    public async Task multiple_tag_values_and_together(DcbStorageMode mode)
    {
        ConfigureStore(mode);

        var student = new StudentId(Guid.NewGuid());
        var course = new CourseId(Guid.NewGuid());

        var both = theSession.Events.BuildEvent(new StudentEnrolled("Alice", "Math"));
        both.WithTag(student);
        both.WithTag(course);
        theSession.Events.Append(Guid.NewGuid(), both);

        var studentOnly = theSession.Events.BuildEvent(new AssignmentSubmitted("HW1", 90));
        studentOnly.WithTag(student);
        theSession.Events.Append(Guid.NewGuid(), studentOnly);

        var courseOnly = theSession.Events.BuildEvent(new AssignmentSubmitted("HW2", 80));
        courseOnly.WithTag(course);
        theSession.Events.Append(Guid.NewGuid(), courseOnly);

        await theSession.SaveChangesAsync();

        var result = await queryAsync(new EventQuery
        {
            TagValues =
            {
                ["student"] = student.Value.ToString(),
                ["course"] = course.Value.ToString()
            },
            PageSize = 1000
        });

        result.TotalCount.ShouldBe(1);
        result.Events.Single().Data.ShouldBeOfType<StudentEnrolled>().StudentName.ShouldBe("Alice");
    }

    // ---- the dictionary QueryByTagsAsync overload, which had no coverage at all ----

    private async Task<List<EventRecord>> queryByTagsAsync(IReadOnlyDictionary<string, string> tags)
    {
        var records = new List<EventRecord>();
        await foreach (var record in ((IEventStore)theStore).QueryByTagsAsync(tags, CancellationToken.None))
        {
            records.Add(record);
        }

        return records;
    }

    /// <summary>
    /// Either spelling of the tag name resolves — the CLR type name (<c>StudentId</c>) or the
    /// registered table suffix (<c>student</c>), case-insensitively. Before #5365 only the first
    /// worked here, while Polecat took both.
    /// </summary>
    [Fact]
    public async Task query_by_tags_accepts_either_spelling_of_the_name()
    {
        ConfigureStore(DcbStorageMode.TagTables);
        var matching = await seedAsync();

        foreach (var name in new[] { "StudentId", "student", "STUDENTID", "Student" })
        {
            var records = await queryByTagsAsync(new Dictionary<string, string>
            {
                [name] = matching.Value.ToString()
            });

            records.Count.ShouldBe(2, $"the tag name spelling '{name}' should have resolved");
        }
    }

    [Fact]
    public async Task query_by_tags_matches_a_value_regardless_of_casing()
    {
        ConfigureStore(DcbStorageMode.TagTables);
        var matching = await seedAsync();

        var records = await queryByTagsAsync(new Dictionary<string, string>
        {
            ["student"] = matching.Value.ToString().ToUpperInvariant()
        });

        records.Count.ShouldBe(2);
    }

    /// <summary>
    /// An unregistered name stays an error rather than an empty answer, and the message still names
    /// what is registered so the caller can correct a typo from it.
    /// </summary>
    [Fact]
    public async Task query_by_tags_refuses_an_unregistered_name()
    {
        ConfigureStore(DcbStorageMode.TagTables);
        await seedAsync();

        var ex = await Should.ThrowAsync<ArgumentException>(async () =>
            await queryByTagsAsync(new Dictionary<string, string> { ["no_such_tag"] = "whatever" }));

        ex.Message.ShouldContain("no_such_tag");
        ex.Message.ShouldContain(nameof(StudentId));
    }
}
