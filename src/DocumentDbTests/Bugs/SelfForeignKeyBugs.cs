using System;
using Marten.Testing.Harness;
using Xunit;

namespace DocumentDbTests.Bugs;

public class SelfForeignKeyBugs(DefaultStoreFixture fixture) : IntegrationContext(fixture)
{
    [Fact]
    public void unitofwork_sort_doesnt_break_self_foreign_keys()
    {
        StoreOptions(o =>
        {
            o.Schema.For<Folder>().ForeignKey<Folder>(f => f.ParentId);
            o.Schema.For<File>().ForeignKey<Folder>(f => f.FolderId);
        });

        using var session = theStore.LightweightSession();
        for (var i = 0; i < 30; i++)
        {
            var folder = new Folder { Name = $"Folder {i}" };
            session.Store(folder);

            for (var j = 0; j < 5; j++)
            {
                var subFolder = new Folder { Name = $"Subfolder {j}", ParentId = folder.Id };
                session.Store(subFolder);

                for (var k = 0; k < 5; k++)
                {
                    session.Store(new File { Name = $"File {k}", FolderId = subFolder.Id });
                }
            }
        }

        session.SaveChanges();
    }
}

public class Folder
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public Guid? ParentId { get; set; }
}

public class File
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public Guid FolderId { get; set; }
}
