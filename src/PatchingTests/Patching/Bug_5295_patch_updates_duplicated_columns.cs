#nullable enable
using System;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Marten;
using Marten.Patching;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace PatchingTests.Patching;

/// <summary>
/// #5295. A patch that moves a value the schema also keeps in a <c>.Duplicate()</c> column has to refresh
/// that column, or the document and the index that exists to search it disagree — <c>Load</c> returns the
/// new value and <c>Query</c> filtered on the same member returns nothing, with no error and nothing in
/// the row to show why.
/// <para>
/// Marten already did this for the straightforward case, so the issue's headline ("patches never update
/// duplicated columns") is not what the code does: a patch whose path is exactly the duplicated member's
/// has refreshed the column since #2995. What was broken is which patches counted as affecting a field —
/// three separate gaps, each verified failing before the fix.
/// </para>
/// </summary>
public class Bug_5295_patch_updates_duplicated_columns: OneOffConfigurationsContext
{
    // The schema name defaults to the class name and feeds every table, constraint and index name under
    // it, so with a name this long a duplicated column's index runs past PostgreSQL's 63-character limit.
    public Bug_5295_patch_updates_duplicated_columns() => _schemaName = "bug5295";

    // Every test uses the SAME mapping, deliberately. OneOffConfigurationsContext gives the whole class
    // one schema, so varying the duplicated columns per test leaves the table from the previous test in
    // place and the next CREATE INDEX collides with 42P07.
    private void ConfigureStore()
    {
        StoreOptions(opts =>
        {
            opts.UseSystemTextJsonForSerialization();

            opts.Schema.For<Doc>()
                .DocumentAlias("doc5295")
                .Duplicate(x => x.Status)
                .Duplicate(x => x.AliasedStatus)
                .Duplicate(x => x.Copy)
                .Duplicate(x => x.StatusCode)
                .Duplicate(x => x.Job.Progress);
        });
    }

    public class Doc
    {
        public Guid Id { get; set; }

        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("st")]
        public string AliasedStatus { get; set; } = string.Empty;

        public JobState Job { get; set; } = new();

        public string Copy { get; set; } = string.Empty;

        // Deliberately a prefix-neighbour of Status: the old matching test was a bare StartsWith with no
        // separator boundary, so these two overlapped each other.
        public string StatusCode { get; set; } = string.Empty;
    }

    public class JobState
    {
        public int Progress { get; set; }
    }

    private async Task<Guid> SeedAsync(Action<Doc> configure)
    {
        var doc = new Doc { Id = Guid.NewGuid() };
        configure(doc);

        await using var session = theStore.LightweightSession();
        session.Store(doc);
        await session.SaveChangesAsync();

        return doc.Id;
    }

    [Fact]
    public async Task the_straightforward_case_already_worked()
    {
        // The issue's headline repro. Pinned as the baseline the three gaps below are measured against,
        // and because a fix to the matching rule could easily break it.
        ConfigureStore();
        var id = await SeedAsync(d => d.Status = "open");

        await using (var session = theStore.LightweightSession())
        {
            session.Patch<Doc>(id).Set(x => x.Status, "done");
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            (await session.LoadAsync<Doc>(id))!.Status.ShouldBe("done");
            (await session.Query<Doc>().Where(x => x.Status == "done").ToListAsync()).Count.ShouldBe(1);
        }
    }

    [Fact]
    public async Task an_aliased_member_is_matched_by_its_serialized_name()
    {
        // Gap 1, and a regression #5292 introduced. That PR made PatchExpression.toPath resolve serializer
        // aliases, so the patch now correctly writes "st" -- but the affected-field test still computed
        // "AliasedStatus" from the raw member name, found no overlap, and skipped the column refresh.
        // Before #5292 the two agreed by both being wrong: the patch missed the aliased node as well, so
        // document and column stayed consistent with each other while both ignored the patch.
        ConfigureStore();
        var id = await SeedAsync(d => d.AliasedStatus = "open");

        await using (var session = theStore.LightweightSession())
        {
            session.Patch<Doc>(id).Set(x => x.AliasedStatus, "done");
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            (await session.LoadAsync<Doc>(id))!.AliasedStatus.ShouldBe("done");
            (await session.Query<Doc>().Where(x => x.AliasedStatus == "done").ToListAsync()).Count.ShouldBe(1);
        }
    }

    [Fact]
    public async Task patching_a_parent_refreshes_a_deeper_duplicated_column()
    {
        // Gap 2. The match was a one-way prefix test -- "does a patch path start with the field path" --
        // so it saw a patch on Job.Progress affecting a column duplicated from Job, but not a patch on
        // Job affecting a column duplicated from Job.Progress. Replacing the parent moves everything
        // underneath it, so that is the direction that actually loses data from the index.
        ConfigureStore();
        var id = await SeedAsync(d => d.Job = new JobState { Progress = 1 });

        await using (var session = theStore.LightweightSession())
        {
            session.Patch<Doc>(id).Set(x => x.Job, new JobState { Progress = 42 });
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            (await session.LoadAsync<Doc>(id))!.Job.Progress.ShouldBe(42);
            (await session.Query<Doc>().Where(x => x.Job.Progress == 42).ToListAsync()).Count.ShouldBe(1);
        }
    }

    [Fact]
    public async Task the_destination_of_a_duplicate_operation_is_refreshed()
    {
        // Gap 3. "path" is not the only place a patch writes: the patching API's own Duplicate copies a
        // value to other locations, carried in "targets", and only "path" was ever collected. (Rename is
        // the same shape, writing to a sibling named by "to".)
        ConfigureStore();
        var id = await SeedAsync(d =>
        {
            d.Status = "open";
            d.Copy = "stale";
        });

        await using (var session = theStore.LightweightSession())
        {
            session.Patch<Doc>(id).Duplicate(x => x.Status, x => x.Copy);
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            (await session.LoadAsync<Doc>(id))!.Copy.ShouldBe("open");
            (await session.Query<Doc>().Where(x => x.Copy == "open").ToListAsync()).Count.ShouldBe(1);
        }
    }

    [Fact]
    public async Task a_prefix_neighbour_is_not_confused_for_the_duplicated_member()
    {
        // Status and StatusCode share a prefix. Under the old bare StartsWith they matched each other,
        // which only ever cost a redundant column refresh -- but the boundary check that fixes gap 2 has
        // to keep both columns correct rather than trading one bug for another.
        ConfigureStore();
        var id = await SeedAsync(d =>
        {
            d.Status = "open";
            d.StatusCode = "A1";
        });

        await using (var session = theStore.LightweightSession())
        {
            session.Patch<Doc>(id).Set(x => x.StatusCode, "B2");
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            (await session.Query<Doc>().Where(x => x.StatusCode == "B2").ToListAsync()).Count.ShouldBe(1);
            (await session.Query<Doc>().Where(x => x.Status == "open").ToListAsync()).Count.ShouldBe(1);
        }
    }
}
