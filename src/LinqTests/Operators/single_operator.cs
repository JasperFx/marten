using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit.Abstractions;

namespace LinqTests.Operators;

public class single_operator : IntegrationContext
{
    private readonly ITestOutputHelper _output;

    #region sample_single_and_single_or_default
    [Fact]
    public void single_hit_with_only_one_document()
    {
        theSession.Store(new Target{Number = 1});
        theSession.Store(new Target{Number = 2});
        theSession.Store(new Target{Number = 3});
        theSession.Store(new Target{Number = 4});
        theSession.SaveChanges();

        SpecificationExtensions.ShouldNotBeNull(theSession.Query<Target>().Single(x => x.Number == 3));
    }

    [Fact]
    public void single_or_default_hit_with_only_one_document()
    {
        theSession.Store(new Target { Number = 1 });
        theSession.Store(new Target { Number = 2 });
        theSession.Store(new Target { Number = 3 });
        theSession.Store(new Target { Number = 4 });
        theSession.SaveChanges();

        SpecificationExtensions.ShouldNotBeNull(theSession.Query<Target>().SingleOrDefault(x => x.Number == 3));
    }
    #endregion

    [Fact]
    public void single_or_default_miss()
    {
        theSession.Store(new Target { Number = 1 });
        theSession.Store(new Target { Number = 2 });
        theSession.Store(new Target { Number = 3 });
        theSession.Store(new Target { Number = 4 });
        theSession.SaveChanges();

        SpecificationExtensions.ShouldBeNull(theSession.Query<Target>().SingleOrDefault(x => x.Number == 11));
    }

    [Fact]
    public void single_hit_with_more_than_one_match()
    {
        theSession.Store(new Target { Number = 1 });
        theSession.Store(new Target { Number = 2 });
        theSession.Store(new Target { Number = 2 });
        theSession.Store(new Target { Number = 4 });
        theSession.SaveChanges();

        Exception<InvalidOperationException>.ShouldBeThrownBy(() =>
        {
            theSession.Query<Target>().Where(x => x.Number == 2).Single();
        });
    }

    [Fact]
    public void single_hit_with_more_than_one_match_and_take_one_should_not_throw()
    {
        theSession.Logger = new TestOutputMartenLogger(_output);

        theSession.Store(new Target { Number = 1 });
        theSession.Store(new Target { Number = 2 });
        theSession.Store(new Target { Number = 2 });
        theSession.Store(new Target { Number = 4 });
        theSession.SaveChanges();

        theSession.Query<Target>().Where(x => x.Number == 2).Take(1).Single().ShouldNotBeNull();
    }

    [Fact]
    public void single_or_default_hit_with_more_than_one_match()
    {
        theSession.Store(new Target { Number = 1 });
        theSession.Store(new Target { Number = 2 });
        theSession.Store(new Target { Number = 2 });
        theSession.Store(new Target { Number = 4 });
        theSession.SaveChanges();

        Exception<InvalidOperationException>.ShouldBeThrownBy(() =>
        {
            theSession.Query<Target>().Where(x => x.Number == 2).SingleOrDefault();
        });
    }

    [Fact]
    public void single_miss()
    {
        theSession.Store(new Target { Number = 1 });
        theSession.Store(new Target { Number = 2 });
        theSession.Store(new Target { Number = 3 });
        theSession.Store(new Target { Number = 4 });
        theSession.SaveChanges();

        Exception<InvalidOperationException>.ShouldBeThrownBy(() =>
        {
            theSession.Query<Target>().Where(x => x.Number == 11).Single();
        });
    }

        [Fact]
    public async Task single_hit_with_only_one_document_async()
    {
        theSession.Store(new Target { Number = 1 });
        theSession.Store(new Target { Number = 2 });
        theSession.Store(new Target { Number = 3 });
        theSession.Store(new Target { Number = 4 });
        await theSession.SaveChangesAsync();

        var target = await theSession.Query<Target>().SingleAsync(x => x.Number == 3);
        target.ShouldNotBeNull();
    }

    [Fact]
    public async Task single_or_default_hit_with_only_one_document_async()
    {
        theSession.Store(new Target { Number = 1 });
        theSession.Store(new Target { Number = 2 });
        theSession.Store(new Target { Number = 3 });
        theSession.Store(new Target { Number = 4 });
        await theSession.SaveChangesAsync();

        var target = await theSession.Query<Target>().SingleOrDefaultAsync(x => x.Number == 3);
        target.ShouldNotBeNull();
    }

    [Fact]
    public async Task single_or_default_miss_async()
    {
        theSession.Store(new Target { Number = 1 });
        theSession.Store(new Target { Number = 2 });
        theSession.Store(new Target { Number = 3 });
        theSession.Store(new Target { Number = 4 });
        await theSession.SaveChangesAsync();

        var target = await theSession.Query<Target>().SingleOrDefaultAsync(x => x.Number == 11);
        target.ShouldBeNull();
    }

    [Fact]
    public async Task single_hit_with_more_than_one_match_async()
    {
        theSession.Store(new Target { Number = 1 });
        theSession.Store(new Target { Number = 2 });
        theSession.Store(new Target { Number = 2 });
        theSession.Store(new Target { Number = 4 });
        await theSession.SaveChangesAsync();

        await Exception<InvalidOperationException>.ShouldBeThrownByAsync(async () =>
        {
            await theSession.Query<Target>().Where(x => x.Number == 2).SingleAsync();
        });
    }

    [Fact]
    public async Task single_hit_with_more_than_one_match_and_take_one_should_not_throw_async()
    {
        theSession.Store(new Target { Number = 1 });
        theSession.Store(new Target { Number = 2 });
        theSession.Store(new Target { Number = 2 });
        theSession.Store(new Target { Number = 4 });
        await theSession.SaveChangesAsync();

        var target = await theSession.Query<Target>().Where(x => x.Number == 2).Take(1).SingleAsync();
        target.ShouldNotBeNull();
    }

    [Fact]
    public async Task single_or_default_hit_with_more_than_one_match_async()
    {
        theSession.Store(new Target { Number = 1 });
        theSession.Store(new Target { Number = 2 });
        theSession.Store(new Target { Number = 2 });
        theSession.Store(new Target { Number = 4 });
        await theSession.SaveChangesAsync();

        await Exception<InvalidOperationException>.ShouldBeThrownByAsync(async () =>
        {
            await theSession.Query<Target>().Where(x => x.Number == 2).SingleOrDefaultAsync();
        });
    }

    [Fact]
    public async Task single_miss_async()
    {
        theSession.Store(new Target { Number = 1 });
        theSession.Store(new Target { Number = 2 });
        theSession.Store(new Target { Number = 3 });
        theSession.Store(new Target { Number = 4 });
        await theSession.SaveChangesAsync();

        await Exception<InvalidOperationException>.ShouldBeThrownByAsync(async () =>
        {
            await theSession.Query<Target>().Where(x => x.Number == 11).SingleAsync();
        });
    }

    public single_operator(DefaultStoreFixture fixture, ITestOutputHelper output) : base(fixture)
    {
        _output = output;
    }
}
