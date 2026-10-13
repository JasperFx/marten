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
        StoreOptions(opts =>
        {
            if (duplicatedField)
            {
                opts.Schema.For<Folder>().Duplicate(x => x.Name);
            }
        });

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

        await using var query = kind switch
        {
            SessionKind.Query => theStore.QuerySession(),
            SessionKind.Lightweight => theStore.LightweightSession(),
            SessionKind.IdentityMap => theStore.IdentitySession(),
            _ => (IQuerySession)theStore.DirtyTrackedSession()
        };

        var children = new List<FolderItem>();
        var folders = await query.Query<Folder>()
            .Where(x => x.Name == "Invoices")
            .Include(children).On(x => x.Id, i => i.FolderId)
            .ToListAsync();

        folders.Single().Id.ShouldBe(folder.Id);
        children.Select(x => x.Name).OrderBy(x => x).ShouldBe(["one", "two"]);
    }

    public class Folder
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
    }

    public class FolderItem
    {
        public Guid Id { get; set; }
        public Guid FolderId { get; set; }
        public string Name { get; set; } = "";
    }
}
