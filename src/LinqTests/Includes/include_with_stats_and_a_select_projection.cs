#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
using Shouldly;

namespace LinqTests.Includes;

/// <summary>
/// #5654. With an Include() the query is materialised into a temp table, and the statistics column
/// goes in with it -- correctly, because PostgreSQL evaluates a window function before LIMIT. Reading
/// the rows back then has two paths, and the one a Select() projection takes used to re-apply the
/// clause, emitting a second count(*) over() against a temp table the inner statement's Take() had
/// already truncated. The count came back as the size of the page.
/// </summary>
public class include_with_stats_and_a_select_projection: OneOffConfigurationsContext
{
    public enum SessionKind
    {
        Query,
        Lightweight,
        IdentityMap,
        DirtyTracked
    }

    [Theory]
    [InlineData(SessionKind.Query)]
    [InlineData(SessionKind.Lightweight)]
    [InlineData(SessionKind.IdentityMap)]
    [InlineData(SessionKind.DirtyTracked)]
    public async Task a_projected_page_still_counts_every_match(SessionKind kind)
    {
        await seedAsync();

        await using var query = sessionFor(kind);
        var children = new List<FolderItem>();

        var page = await query.Query<Folder>()
            .Stats(out var stats)
            .Where(x => x.Name == "Invoices")
            .Include(children).On(x => x.Id, i => i.FolderId)
            .OrderBy(x => x.Sort)
            .Take(2)
            .Select(x => x.Sort)
            .ToListAsync();

        page.ShouldBe([1, 2]);
        stats.TotalResults.ShouldBe(3);

        // The include follows the page, not the match set: it reads from the same truncated temp
        // table. Only the statistics column is expected to outlive the Take().
        children.Count.ShouldBe(2);
    }

    /// <summary>
    /// A projection into a type rather than a scalar takes the same path, and is what a paged grid
    /// of summaries actually looks like.
    /// </summary>
    [Fact]
    public async Task a_projection_into_a_named_type_counts_every_match()
    {
        await seedAsync();

        await using var query = theStore.QuerySession();
        var children = new List<FolderItem>();

        var page = await query.Query<Folder>()
            .Stats(out var stats)
            .Where(x => x.Name == "Invoices")
            .Include(children).On(x => x.Id, i => i.FolderId)
            .OrderBy(x => x.Sort)
            .Take(2)
            .Select(x => new FolderSummary { Sort = x.Sort, Name = x.Name })
            .ToListAsync();

        page.Select(x => x.Sort).ShouldBe([1, 2]);
        stats.TotalResults.ShouldBe(3);
    }

    /// <summary>
    /// Taking every match is the shape that passed while the count was broken: the page and the
    /// total coincide, so it says nothing on its own. Here to keep the whole-set case honest.
    /// </summary>
    [Fact]
    public async Task a_projection_over_the_whole_set_is_unchanged()
    {
        await seedAsync();

        await using var query = theStore.QuerySession();
        var children = new List<FolderItem>();

        var all = await query.Query<Folder>()
            .Stats(out var stats)
            .Where(x => x.Name == "Invoices")
            .Include(children).On(x => x.Id, i => i.FolderId)
            .OrderBy(x => x.Sort)
            .Select(x => x.Sort)
            .ToListAsync();

        all.ShouldBe([1, 2, 3]);
        stats.TotalResults.ShouldBe(3);
    }

    private async Task seedAsync()
    {
        StoreOptions(_ => { });

        var folders = Enumerable.Range(1, 3)
            .Select(i => new Folder { Id = Guid.NewGuid(), Name = "Invoices", Sort = i })
            .ToArray();
        var other = new Folder { Id = Guid.NewGuid(), Name = "Receipts", Sort = 9 };

        await using var session = theStore.LightweightSession();
        session.Store(folders);
        session.Store(other);
        session.Store(folders.Select(f => new FolderItem { Id = Guid.NewGuid(), FolderId = f.Id }).ToArray());
        await session.SaveChangesAsync();
    }

    private IQuerySession sessionFor(SessionKind kind) => kind switch
    {
        SessionKind.Query => theStore.QuerySession(),
        SessionKind.Lightweight => theStore.LightweightSession(),
        SessionKind.IdentityMap => theStore.IdentitySession(),
        _ => theStore.DirtyTrackedSession()
    };

    public class Folder
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public int Sort { get; set; }
    }

    public class FolderItem
    {
        public Guid Id { get; set; }
        public Guid FolderId { get; set; }
    }

    public class FolderSummary
    {
        public int Sort { get; set; }
        public string Name { get; set; } = "";
    }
}
