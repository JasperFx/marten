using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;

namespace LinqTests.Operators;

public class count_operator: IntegrationContext
{
    [Fact]
    public async Task count_without_any_where()
    {
        theSession.Store(new Target { Number = 1 });
        theSession.Store(new Target { Number = 2 });
        theSession.Store(new Target { Number = 3 });
        theSession.Store(new Target { Number = 4 });
        await theSession.SaveChangesAsync();

        theSession.Query<Target>().Count().ShouldBe(4);
    }

    [Fact]
    public async Task count_ignores_order_by()
    {
        theSession.Store(new Target { Number = 1 });
        theSession.Store(new Target { Number = 2 });
        theSession.Store(new Target { Number = 3 });
        theSession.Store(new Target { Number = 4 });
        await theSession.SaveChangesAsync();

        (await theSession.Query<Target>().OrderBy(x => x.Number).CountAsync()).ShouldBe(4);
    }

    [Fact]
    public async Task long_count_without_any_where()
    {
        theSession.Store(new Target { Number = 1 });
        theSession.Store(new Target { Number = 2 });
        theSession.Store(new Target { Number = 3 });
        theSession.Store(new Target { Number = 4 });
        await theSession.SaveChangesAsync();

        theSession.Query<Target>().LongCount().ShouldBe(4);
    }

    [Fact]
    public async Task count_matching_properties_within_type()
    {
        var t1 = new Target();
        t1.OtherGuid = t1.Id;
        var t2 = new Target();
        t2.OtherGuid = t2.Id;

        theSession.Store(t1);
        theSession.Store(t2);
        theSession.Store(new Target());
        theSession.Store(new Target());
        await theSession.SaveChangesAsync();
        theSession.Query<Target>().Count(x => x.Id == x.OtherGuid).ShouldBe(2);
    }

    [Fact]
    public async Task count_matching_properties_within_type_notequals()
    {
        var t1 = new Target();
        t1.OtherGuid = t1.Id;
        var t2 = new Target();
        t2.OtherGuid = t2.Id;

        theSession.Store(t1);
        theSession.Store(t2);
        theSession.Store(new Target());
        theSession.Store(new Target());
        await theSession.SaveChangesAsync();
        theSession.Query<Target>().Count(x => x.Id != x.OtherGuid).ShouldBe(2);
    }

    // Well, this is pretty much a redundant test (since we're testing the Linq translation) but covers #1067
    [Fact]
    public async Task count_matching_properties_within_type_async()
    {
        var t1 = new Target();
        t1.OtherGuid = t1.Id;
        var t2 = new Target();
        t2.OtherGuid = t2.Id;

        theSession.Store(t1);
        theSession.Store(t2);
        theSession.Store(new Target());
        theSession.Store(new Target());
        await theSession.SaveChangesAsync();
        var count = await theSession.Query<Target>().CountAsync(x => x.Id == x.OtherGuid);
        count.ShouldBe(2);
    }

    [Fact]
    public async Task long_count_with_a_where_clause()
    {
        // theSession is an IDocumentSession in this test
        theSession.Store(new Target { Number = 1 });
        theSession.Store(new Target { Number = 2 });
        theSession.Store(new Target { Number = 3 });
        theSession.Store(new Target { Number = 4 });
        theSession.Store(new Target { Number = 5 });
        theSession.Store(new Target { Number = 6 });
        await theSession.SaveChangesAsync();

        theSession.Query<Target>().LongCount(x => x.Number > 3).ShouldBe(3);
    }

    [Fact]
    #region sample_using_count
    public async Task count_with_a_where_clause()
    {
        // theSession is an IDocumentSession in this test
        theSession.Store(new Target { Number = 1 });
        theSession.Store(new Target { Number = 2 });
        theSession.Store(new Target { Number = 3 });
        theSession.Store(new Target { Number = 4 });
        theSession.Store(new Target { Number = 5 });
        theSession.Store(new Target { Number = 6 });
        await theSession.SaveChangesAsync();

        theSession.Query<Target>().Count(x => x.Number > 3).ShouldBe(3);
    }

    #endregion


    [Fact]
    public async Task count_without_any_where_async()
    {
        theSession.Store(new Target { Number = 1 });
        theSession.Store(new Target { Number = 2 });
        theSession.Store(new Target { Number = 3 });
        theSession.Store(new Target { Number = 4 });
        await theSession.SaveChangesAsync();

        var result = await theSession.Query<Target>().CountAsync();
        result.ShouldBe(4);
    }

    [Fact]
    public async Task long_count_without_any_where_async()
    {
        theSession.Store(new Target { Number = 1 });
        theSession.Store(new Target { Number = 2 });
        theSession.Store(new Target { Number = 3 });
        theSession.Store(new Target { Number = 4 });
        await theSession.SaveChangesAsync();

        var result = await theSession.Query<Target>().LongCountAsync();
        result.ShouldBe(4);
    }

    [Fact]
    public async Task count_with_a_where_clause_async()
    {
        theSession.Store(new Target { Number = 1 });
        theSession.Store(new Target { Number = 2 });
        theSession.Store(new Target { Number = 3 });
        theSession.Store(new Target { Number = 4 });
        theSession.Store(new Target { Number = 5 });
        theSession.Store(new Target { Number = 6 });
        await theSession.SaveChangesAsync();

        var result = await theSession.Query<Target>().CountAsync(x => x.Number > 3);
        result.ShouldBe(3);
    }

    [Fact]
    public async Task long_count_with_a_where_clause_async()
    {
        theSession.Store(new Target { Number = 1 });
        theSession.Store(new Target { Number = 2 });
        theSession.Store(new Target { Number = 3 });
        theSession.Store(new Target { Number = 4 });
        theSession.Store(new Target { Number = 5 });
        theSession.Store(new Target { Number = 6 });
        await theSession.SaveChangesAsync();

        var result = await theSession.Query<Target>().LongCountAsync(x => x.Number > 3);
        result.ShouldBe(3);
    }

    public count_operator(DefaultStoreFixture fixture) : base(fixture)
    {
    }
}
