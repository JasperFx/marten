using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Schema;
using Marten.Testing.Harness;

namespace LinqTests.Bugs;

public class child_parent_inclusion_bug : BugIntegrationContext
{
    [Fact]
    public async Task WhereStatementIsRespectedWhenIncludingParent()
    {
        var parentId = Guid.NewGuid();
        var childId = Guid.NewGuid();

        theSession.Insert(new Parent(parentId));
        theSession.Insert(new Parent(Guid.NewGuid()));

        theSession.Insert(new Child
        {
            Id = childId,
            ParentId = parentId
        });

        theSession.Insert(new Child
        {
            Id = Guid.NewGuid(),
            ParentId = parentId
        });

        await theSession.SaveChangesAsync();

        var parents = new Dictionary<Guid, Parent>();

        var items = await theSession.Query<Child>()
            .Include(q => q.ParentId, parents)  // Removing this line gets the test to pass!
            .Where(x => x.Id == childId)
            .ToListAsync();

        Assert.Single(items);
    }

    public record Parent(Guid Id);

    public record Child
    {
        public Guid Id { get; init; }

        [ForeignKey(typeof(Parent))]
        public Guid ParentId { get; init; }
    };
}
