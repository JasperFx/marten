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
/// An include joined on the queried document's own id: every child whose parent id names the
/// document the query returned. The temp table the include reads from must carry that id
/// whichever session runs the query and whether or not the queried document has duplicated fields.
/// </summary>
public class include_on_the_queried_document_id: OneOffConfigurationsContext
{
    public enum SessionKind
    {
        Query,
        Lightweight,
        IdentityMap,
        DirtyTracked
    }

    [Theory]
    [InlineData(SessionKind.Query, true)]
    [InlineData(SessionKind.Lightweight, true)]
    [InlineData(SessionKind.IdentityMap, true)]
    [InlineData(SessionKind.DirtyTracked, true)]
    [InlineData(SessionKind.Query, false)]
    [InlineData(SessionKind.Lightweight, false)]
    [InlineData(SessionKind.IdentityMap, false)]
    [InlineData(SessionKind.DirtyTracked, false)]
    public async Task includes_the_children_of_the_queried_document(SessionKind kind, bool duplicatedField)
    {
        configure(duplicatedField);

        var folder = new Folder { Id = Guid.NewGuid(), Name = "Invoices" };
        var other = new Folder { Id = Guid.NewGuid(), Name = "Receipts" };
        var items = new[]
        {
            new FolderItem { Id = Guid.NewGuid(), FolderId = folder.Id, Name = "one" },
            new FolderItem { Id = Guid.NewGuid(), FolderId = folder.Id, Name = "two" },
            new FolderItem { Id = Guid.NewGuid(), FolderId = other.Id, Name = "elsewhere" }
        };

        await using (var session = theStore.LightweightSession())
        {
            session.Store(folder, other);
            session.Store(items);
            await session.SaveChangesAsync();
        }

        await using var query = sessionFor(kind);

        var children = new List<FolderItem>();
        var folders = await query.Query<Folder>()
            .Where(x => x.Name == "Invoices")
            .Include(children).On(x => x.Id, i => i.FolderId)
            .ToListAsync();

        folders.Single().Id.ShouldBe(folder.Id);
        children.Select(x => x.Name).OrderBy(x => x).ShouldBe(["one", "two"]);
    }

    /// <summary>
    /// #5653: Stats() wraps the select clause, which hid it from the check that puts the id back
    /// into the temp table. Returning fewer rows than match keeps the assertion on TotalResults
    /// honest about where the statistics column lands once the id column is added alongside it.
    /// </summary>
    [Theory]
    [InlineData(SessionKind.Query, true)]
    [InlineData(SessionKind.Lightweight, true)]
    [InlineData(SessionKind.IdentityMap, true)]
    [InlineData(SessionKind.DirtyTracked, true)]
    [InlineData(SessionKind.Query, false)]
    [InlineData(SessionKind.Lightweight, false)]
    [InlineData(SessionKind.IdentityMap, false)]
    [InlineData(SessionKind.DirtyTracked, false)]
    public async Task includes_the_children_and_counts_every_match(SessionKind kind, bool duplicatedField)
    {
        configure(duplicatedField);

        var folders = Enumerable.Range(1, 3)
            .Select(i => new Folder { Id = Guid.NewGuid(), Name = "Invoices", Sort = i })
            .ToArray();
        var other = new Folder { Id = Guid.NewGuid(), Name = "Receipts", Sort = 4 };
        var items = new[]
        {
            new FolderItem { Id = Guid.NewGuid(), FolderId = folders[0].Id, Name = "one" },
            new FolderItem { Id = Guid.NewGuid(), FolderId = folders[1].Id, Name = "two" },
            new FolderItem { Id = Guid.NewGuid(), FolderId = other.Id, Name = "elsewhere" }
        };

        await using (var session = theStore.LightweightSession())
        {
            session.Store(folders);
            session.Store(other);
            session.Store(items);
            await session.SaveChangesAsync();
        }

        await using var query = sessionFor(kind);

        var children = new List<FolderItem>();
        var page = await query.Query<Folder>()
            .Stats(out var stats)
            .Where(x => x.Name == "Invoices")
            .Include(children).On(x => x.Id, i => i.FolderId)
            .OrderBy(x => x.Sort)
            .Take(2)
            .ToListAsync();

        page.Select(x => x.Sort).ShouldBe([1, 2]);
        stats.TotalResults.ShouldBe(3);
        children.Select(x => x.Name).OrderBy(x => x).ShouldBe(["one", "two"]);
    }

    private void configure(bool duplicatedField)
    {
        StoreOptions(opts =>
        {
            if (duplicatedField)
            {
                opts.Schema.For<Folder>().Duplicate(x => x.Name);
            }
        });
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
        public string Name { get; set; } = "";
    }
}
