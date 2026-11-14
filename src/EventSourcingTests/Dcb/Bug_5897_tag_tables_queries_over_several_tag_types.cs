#nullable enable
using System;
using System.Linq;
using JasperFx;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Tags;
using Marten;
using Marten.Events;
using Marten.Storage;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Dcb;

public record Bug5897Student(Guid Value);
public record Bug5897Course(Guid Value);
public record Bug5897Enrolled(string Name);
public record Bug5897Dropped(string Name);

[BoundaryAggregate]
public class Bug5897Folds
{
    public int Folded { get; set; }
    public void Apply(Bug5897Enrolled _) => Folded++;
    public void Apply(Bug5897Dropped _) => Folded++;
}

/// <summary>
/// #5897: under TagTables, a tag query used to LEFT JOIN every tag table it named onto mt_events with the
/// OR above the joins, which PostgreSQL can only answer by reading the whole store (328 ms at 1M events).
/// It is now one index probe per condition combined on seq_id. The join also returned an event once per
/// matching tag row, so an event carrying two values of one tag type that the query both named was read
/// -- and folded -- twice.
/// </summary>
[Collection("OneOffs")]
public class Bug_5897_tag_tables_queries_over_several_tag_types: OneOffConfigurationsContext
{
    private void Configure(StoreOptions opts, bool conjoined = false)
    {
        opts.Events.DcbStorageMode = DcbStorageMode.TagTables;
        opts.Events.AddEventType<Bug5897Enrolled>();
        opts.Events.AddEventType<Bug5897Dropped>();
        opts.Events.RegisterTagType<Bug5897Student>("bug5897_student");
        opts.Events.RegisterTagType<Bug5897Course>("bug5897_course");
        if (conjoined) opts.Events.TenancyStyle = TenancyStyle.Conjoined;
    }

    private static async Task Append(IDocumentStore store, string tenant, object data, params object[] tags)
    {
        await using var session = store.LightweightSession(tenant);
        var e = session.Events.BuildEvent(data);
        e.WithTag(tags);
        session.Events.Append(Guid.NewGuid(), e);
        await session.SaveChangesAsync();
    }

    [Fact]
    public async Task an_event_matched_by_two_values_of_one_tag_type_is_read_once()
    {
        StoreOptions(opts => Configure(opts));

        var copiedFrom = new Bug5897Student(Guid.NewGuid());
        var copier = new Bug5897Student(Guid.NewGuid());
        await Append(theStore, StorageConstants.DefaultTenantId, new Bug5897Enrolled("homework copied"), copiedFrom,
            copier);

        var query = new EventTagQuery().Or(copiedFrom).Or(copier);

        await using var session = theStore.LightweightSession();
        (await session.Events.QueryByTagsAsync(query)).Count.ShouldBe(1);

        var boundary = await session.Events.FetchForWritingByTags<Bug5897Folds>(query);
        boundary.Events.Count.ShouldBe(1);
        boundary.Aggregate!.Folded.ShouldBe(1);
    }

    [Fact]
    public async Task conditions_over_two_tag_types_select_exactly_their_events()
    {
        StoreOptions(opts => Configure(opts));

        var student = new Bug5897Student(Guid.NewGuid());
        var course = new Bug5897Course(Guid.NewGuid());
        var tenant = StorageConstants.DefaultTenantId;

        await Append(theStore, tenant, new Bug5897Enrolled("student enrolled"), student);
        await Append(theStore, tenant, new Bug5897Dropped("student dropped"), student); // type not selected
        await Append(theStore, tenant, new Bug5897Dropped("course dropped"), course);
        await Append(theStore, tenant, new Bug5897Enrolled("both"), student, course);
        await Append(theStore, tenant, new Bug5897Enrolled("someone else"), new Bug5897Student(Guid.NewGuid()));

        // the student's enrolments, and anything for the course
        var query = new EventTagQuery()
            .Or(student).AndEventsOfType<Bug5897Enrolled>()
            .Or(course);

        await using var session = theStore.LightweightSession();
        var names = (await session.Events.QueryByTagsAsync(query)).Select(Name).ToArray();
        names.ShouldBe(["student enrolled", "course dropped", "both"]);

        var boundary = await session.Events.FetchForWritingByTags<Bug5897Folds>(query);
        boundary.Events.Select(Name).ShouldBe(names);
    }

    [Fact]
    public async Task conditions_over_two_tag_types_stay_within_the_tenant()
    {
        StoreOptions(opts => Configure(opts, conjoined: true));

        var student = new Bug5897Student(Guid.NewGuid());
        var course = new Bug5897Course(Guid.NewGuid());

        await Append(theStore, "tenant-a", new Bug5897Enrolled("a student"), student);
        await Append(theStore, "tenant-a", new Bug5897Enrolled("a course"), course);
        await Append(theStore, "tenant-b", new Bug5897Enrolled("b student"), student);
        await Append(theStore, "tenant-b", new Bug5897Enrolled("b course"), course);

        var query = new EventTagQuery().Or(student).Or(course);

        await using var session = theStore.LightweightSession("tenant-a");
        (await session.Events.QueryByTagsAsync(query)).Select(Name).ShouldBe(["a student", "a course"]);
        (await session.Events.FetchForWritingByTags<Bug5897Folds>(query)).Events.Select(Name)
            .ShouldBe(["a student", "a course"]);
    }

    private static string Name(IEvent e) => e.Data switch
    {
        Bug5897Enrolled x => x.Name,
        Bug5897Dropped x => x.Name,
        _ => throw new InvalidOperationException(e.Data.GetType().Name)
    };
}
