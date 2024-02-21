using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Metadata;
using Marten.Testing.Harness;
using Shouldly;
using Xunit.Abstractions;

namespace LinqTests.Bugs;

public class Bug_using_literal_false_in_where_clause : BugIntegrationContext
{
    private readonly ITestOutputHelper _output;

    public Bug_using_literal_false_in_where_clause(ITestOutputHelper output)
    {
        _output = output;
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

        theSession.Logger = new TestOutputMartenLogger(_output);

        IQueryable<DeletableAggregate> query = this.theSession.Query<DeletableAggregate>();
        query = query.Where(x => !x.Deleted);
        query = query.Where(x => false);
        var actual = await query.ToListAsync();

        actual.ShouldBeEmpty();
    }
}

public class DeletableAggregate: ISoftDeleted
{
    public Guid Id { get; set; }
    public bool Deleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}
