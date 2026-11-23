using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten;

namespace LinqTests.Includes.Samples;

// Compile-only samples for docs/documents/querying/linq/include.md. Nothing here runs as a test;
// the behavior each sample shows is covered by the tests named next to it.

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

public class include_documentation_samples
{
    // Covered by Includes/include_on_the_queried_document_id.cs
    #region sample_include_the_children_of_the_queried_document
    public async Task include_the_children_of_the_queried_document(IQuerySession session)
    {
        var items = new List<FolderItem>();

        // Every FolderItem whose FolderId is the Id of a folder this query returns
        var folders = await session.Query<Folder>()
            .Where(x => x.Name == "Invoices")
            .Include(items).On(x => x.Id, i => i.FolderId)
            .ToListAsync();
    }
    #endregion
}
