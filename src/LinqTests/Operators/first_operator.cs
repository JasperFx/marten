using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit.Abstractions;

namespace LinqTests.Operators;

public class first_operator: IntegrationContext
{
    private readonly ITestOutputHelper _output;


    [Fact]
    public void first_hit_with_only_one_document()
    {
        theSession.Store(new Target { Number = 1 });
        theSession.Store(new Target { Number = 2 });
        theSession.Store(new Target { Number = 3 });
        theSession.Store(new Target { Number = 4 });
        theSession.SaveChanges();

        SpecificationExtensions.ShouldNotBeNull(theSession.Query<Target>().First(x => x.Number == 3));
    }

    [Fact]
    public void first_or_default_hit_with_only_one_document()
    {
        theSession.Store(new Target { Number = 1 });
        theSession.Store(new Target { Number = 2 });
        theSession.Store(new Target { Number = 3 });
        theSession.Store(new Target { Number = 4 });
        theSession.SaveChanges();

        theSession.Query<Target>().FirstOrDefault(x => x.Number == 3).ShouldNotBeNull();
    }

    [Fact]
    public void first_or_default_miss()
    {
        theSession.Logger = new TestOutputMartenLogger(_output);

        theSession.Store(new Target { Number = 1 });
        theSession.Store(new Target { Number = 2 });
        theSession.Store(new Target { Number = 3 });
        theSession.Store(new Target { Number = 4 });
        theSession.SaveChanges();

        theSession.Query<Target>().FirstOrDefault(x => x.Number == 11).ShouldBeNull();
    }

    [Fact]
    public void first_correct_hit_with_more_than_one_match()
    {
        theSession.Store(new Target { Number = 1 });
        theSession.Store(new Target { Number = 2, Flag = true });
        theSession.Store(new Target { Number = 2 });
        theSession.Store(new Target { Number = 4 });
        theSession.SaveChanges();

        theSession.Query<Target>().Where(x => x.Number == 2).First().Flag
            .ShouldBeTrue();
    }

    [Fact]
    public void first_or_default_correct_hit_with_more_than_one_match()
    {
        theSession.Store(new Target { Number = 1 });
        theSession.Store(new Target { Number = 2, Flag = true });
        theSession.Store(new Target { Number = 2 });
        theSession.Store(new Target { Number = 4 });
        theSession.SaveChanges();

        theSession.Query<Target>().Where(x => x.Number == 2).First().Flag
            .ShouldBeTrue();
    }

    [Fact]
    public void first_miss()
    {
        theSession.Store(new Target { Number = 1 });
        theSession.Store(new Target { Number = 2 });
        theSession.Store(new Target { Number = 3 });
        theSession.Store(new Target { Number = 4 });
        theSession.SaveChanges();

        Exception<InvalidOperationException>.ShouldBeThrownBy(() =>
        {
            theSession.Query<Target>().Where(x => x.Number == 11).First();
        });
    }

        [Fact]
    public async Task first_hit_with_only_one_document_async()
    {
        theSession.Store(new Target { Number = 1 });
        theSession.Store(new Target { Number = 2 });
        theSession.Store(new Target { Number = 3 });
        theSession.Store(new Target { Number = 4 });
        await theSession.SaveChangesAsync();

        var target = await theSession.Query<Target>().FirstAsync(x => x.Number == 3);
        SpecificationExtensions.ShouldNotBeNull(target);
    }

    [Fact]
    public async Task first_or_default_hit_with_only_one_document_async()
    {
        theSession.Store(new Target { Number = 1 });
        theSession.Store(new Target { Number = 2 });
        theSession.Store(new Target { Number = 3 });
        theSession.Store(new Target { Number = 4 });
        await theSession.SaveChangesAsync();

        var target = await theSession.Query<Target>().FirstOrDefaultAsync(x => x.Number == 3);
        SpecificationExtensions.ShouldNotBeNull(target);
    }

    [Fact]
    public async Task first_or_default_miss_async()
    {
        theSession.Store(new Target { Number = 1 });
        theSession.Store(new Target { Number = 2 });
        theSession.Store(new Target { Number = 3 });
        theSession.Store(new Target { Number = 4 });
        await theSession.SaveChangesAsync();

        var target = await theSession.Query<Target>().FirstOrDefaultAsync(x => x.Number == 11);
        SpecificationExtensions.ShouldBeNull(target);
    }

    [Fact]
    public async Task first_correct_hit_with_more_than_one_match_async()
    {
        theSession.Store(new Target { Number = 1 });
        theSession.Store(new Target { Number = 2, Flag = true });
        theSession.Store(new Target { Number = 2 });
        theSession.Store(new Target { Number = 4 });
        await theSession.SaveChangesAsync();

        var target = await theSession.Query<Target>().Where(x => x.Number == 2).FirstAsync();
        target.Flag.ShouldBeTrue();
    }

    [Fact]
    public async Task first_or_default_correct_hit_with_more_than_one_match_async()
    {
        theSession.Store(new Target { Number = 1 });
        theSession.Store(new Target { Number = 2, Flag = true });
        theSession.Store(new Target { Number = 2 });
        theSession.Store(new Target { Number = 4 });
        await theSession.SaveChangesAsync();

        var target = await theSession.Query<Target>().Where(x => x.Number == 2).FirstOrDefaultAsync();
        target.Flag.ShouldBeTrue();
    }

    [Fact]
    public async Task first_miss_async()
    {
        theSession.Store(new Target { Number = 1 });
        theSession.Store(new Target { Number = 2 });
        theSession.Store(new Target { Number = 3 });
        theSession.Store(new Target { Number = 4 });
        await theSession.SaveChangesAsync();

        await Exception<InvalidOperationException>.ShouldBeThrownByAsync(async () =>
        {
            await theSession.Query<Target>().Where(x => x.Number == 11).FirstAsync();
        });
    }

    public first_operator(DefaultStoreFixture fixture, ITestOutputHelper output) : base(fixture)
    {
        _output = output;
    }
}
