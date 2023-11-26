using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;

namespace LinqTests.Operators;

public class any_operator: IntegrationContext
{
    [Fact]
    public void any_miss_with_query()
    {
        theSession.Store(new Target { Number = 1 });
        theSession.Store(new Target { Number = 2 });
        theSession.Store(new Target { Number = 3 });
        theSession.Store(new Target { Number = 4 });
        theSession.SaveChanges();

        theSession.Query<Target>().Any(x => x.Number == 11)
            .ShouldBeFalse();
    }

    [Fact]
    public void naked_any_miss()
    {
        theSession.Query<Target>().Any()
            .ShouldBeFalse();
    }

    [Fact]
    public void naked_any_hit()
    {
        theSession.Store(new Target { Number = 1 });
        theSession.Store(new Target { Number = 2 });
        theSession.Store(new Target { Number = 3 });
        theSession.Store(new Target { Number = 4 });
        theSession.SaveChanges();

        theSession.Query<Target>().Any().ShouldBeTrue();
    }

    [Fact]
    public async Task any_should_ignore_order()
    {
        theSession.Store(new Target { Number = 1 });
        theSession.Store(new Target { Number = 2 });
        theSession.Store(new Target { Number = 3 });
        theSession.Store(new Target { Number = 4 });
        await theSession.SaveChangesAsync();

        (await theSession.Query<Target>().OrderBy(x => x.Number).AnyAsync()).ShouldBeTrue();
    }

    [Fact]
    public void any_hit_with_only_one_document()
    {
        theSession.Store(new Target { Number = 1 });
        theSession.Store(new Target { Number = 2 });
        theSession.Store(new Target { Number = 3 });
        theSession.Store(new Target { Number = 4 });
        theSession.SaveChanges();

        theSession.Query<Target>().Any(x => x.Number == 3)
            .ShouldBeTrue();
    }

    [Fact]
    public void any_hit_with_more_than_one_match()
    {
        theSession.Store(new Target { Number = 1 });
        theSession.Store(new Target { Number = 2 });
        theSession.Store(new Target { Number = 2 });
        theSession.Store(new Target { Number = 4 });
        theSession.SaveChanges();

        theSession.Query<Target>().Where(x => x.Number == 2).Any()
            .ShouldBeTrue();
    }

    [Fact]
    public async Task any_miss_with_query_async()
    {
        theSession.Store(new Target { Number = 1 });
        theSession.Store(new Target { Number = 2 });
        theSession.Store(new Target { Number = 3 });
        theSession.Store(new Target { Number = 4 });
        await theSession.SaveChangesAsync();

        var result = await theSession.Query<Target>().AnyAsync(x => x.Number == 11);
        result.ShouldBeFalse();
    }

    [Fact]
    public async Task naked_any_miss_async()
    {
        var result = await theSession.Query<Target>().AnyAsync();
        result.ShouldBeFalse();
    }

    [Fact]
    public async Task naked_any_hit_async()
    {
        theSession.Store(new Target { Number = 1 });
        theSession.Store(new Target { Number = 2 });
        theSession.Store(new Target { Number = 3 });
        theSession.Store(new Target { Number = 4 });
        theSession.SaveChanges();

        var result = await theSession.Query<Target>().AnyAsync();
        result.ShouldBeTrue();
    }

    [Fact]
    public async Task any_hit_with_only_one_document_async()
    {
        theSession.Store(new Target { Number = 1 });
        theSession.Store(new Target { Number = 2 });
        theSession.Store(new Target { Number = 3 });
        theSession.Store(new Target { Number = 4 });
        theSession.SaveChanges();

        var result = await theSession.Query<Target>().AnyAsync(x => x.Number == 3);
        result.ShouldBeTrue();
    }

    [Fact]
    public async Task any_hit_with_more_than_one_match_async()
    {
        theSession.Store(new Target { Number = 1 });
        theSession.Store(new Target { Number = 2 });
        theSession.Store(new Target { Number = 2 });
        theSession.Store(new Target { Number = 4 });
        theSession.SaveChanges();

        var result = await theSession.Query<Target>().Where(x => x.Number == 2).AnyAsync();
        result.ShouldBeTrue();
    }

    public any_operator(DefaultStoreFixture fixture) : base(fixture)
    {
    }
}
