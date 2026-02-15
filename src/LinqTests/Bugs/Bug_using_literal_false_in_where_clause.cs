using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Metadata;
using Marten.Testing.Harness;
using Shouldly;
namespace LinqTests.Bugs;

public class Bug_using_literal_false_in_where_clause : BugIntegrationContext
{

    [Fact]
    public async Task query_soft_deleted_and_false()
    {
        var aggregate1 = new DeletableAggregate
        {
            Id = Guid.NewGuid(),
            Deleted = true,
        };

        theSession.Store(aggregate1);
        var aggregate2 = new DeletableAggregate
        {
            Id = Guid.NewGuid(),
            Deleted = false,
        };

        theSession.Store(aggregate2);
        await theSession.SaveChangesAsync();

        var actual = await theSession.Query<DeletableAggregate>().Where(x => false).ToListAsync();

        actual.ShouldBeEmpty();
    }

    [Fact]
    public async Task return_correct_results()
    {
        var aggregate1 = new DeletableAggregate
        {
            Id = Guid.NewGuid(),
            Deleted = true,
        };

        theSession.Store(aggregate1);
        var aggregate2 = new DeletableAggregate
        {
            Id = Guid.NewGuid(),
            Deleted = false,
        };

        theSession.Store(aggregate2);
        await theSession.SaveChangesAsync();

        IQueryable<DeletableAggregate> query = this.theSession.Query<DeletableAggregate>();
        query = query.Where(x => !x.Deleted);
        query = query.Where(x => false);
        var actual = await query.ToListAsync();

        actual.ShouldBeEmpty();
    }

    [Fact]
    public async Task Bug_2980()
    {
        var aggregate1 = new DeletableAggregate
        {
            Id = Guid.NewGuid(),
            Deleted = true,
        };
        theSession.Store(aggregate1);
        var aggregate2 = new DeletableAggregate
        {
            Id = Guid.NewGuid(),
            Deleted = false,
        };
        theSession.Store(aggregate2);
        await theSession.SaveChangesAsync();

        var actual = await theSession
            .Query<DeletableAggregate>()
            .Where(x => false)
            .ToListAsync();

        actual.ShouldBeEmpty();
    }
}

public class DeletableAggregate: ISoftDeleted
{
    public Guid Id { get; set; }
    public bool Deleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}
