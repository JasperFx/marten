using System;
using System.Linq;
using System.Threading.Tasks;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit.Abstractions;

namespace LinqTests.Acceptance;

public class nullable_types : IntegrationContext
{
    private readonly ITestOutputHelper _output;

    [Fact]
    public async Task query_against_non_null()
    {
        theSession.Store(new Target {NullableNumber = 3});
        theSession.Store(new Target {NullableNumber = 6});
        theSession.Store(new Target {NullableNumber = 7});
        theSession.Store(new Target {NullableNumber = 3});
        theSession.Store(new Target {NullableNumber = 5});

        await theSession.SaveChangesAsync();

        theSession.Query<Target>().Where(x => x.NullableNumber > 4).Count()
            .ShouldBe(3);
    }

    [Fact]
    public async Task query_against_null_1()
    {
        theSession.Store(new Target { NullableNumber = 3 });
        theSession.Store(new Target { NullableNumber = null });
        theSession.Store(new Target { NullableNumber = null });
        theSession.Store(new Target { NullableNumber = 3 });
        theSession.Store(new Target { NullableNumber = null });

        await theSession.SaveChangesAsync();

        theSession.Query<Target>().Where(x => x.NullableNumber == null).Count()
            .ShouldBe(3);
    }

    [Fact]
    public async Task query_against_null_2()
    {
        theSession.Store(new Target { NullableNumber = 3 });
        theSession.Store(new Target { NullableNumber = null });
        theSession.Store(new Target { NullableNumber = null });
        theSession.Store(new Target { NullableNumber = 3 });
        theSession.Store(new Target { NullableNumber = null });

        await theSession.SaveChangesAsync();

        theSession.Query<Target>().Where(x => !x.NullableNumber.HasValue).Count()
            .ShouldBe(3);

    }

    [Fact]
    public async Task query_against_null_3()
    {
        theSession.Store(new Target { NullableBoolean = null });
        theSession.Store(new Target { NullableBoolean = true });

        await theSession.SaveChangesAsync();

        theSession.Query<Target>().Where(x => !x.NullableBoolean.HasValue).Count()
            .ShouldBe(1);
    }

    [Fact]
    public async Task query_against_null_4()
    {
        theSession.Store(new Target { NullableDateTime = new DateTime(2526,1,1) });
        theSession.Store(new Target { NullableDateTime = null });
        theSession.Store(new Target { NullableDateTime = null });
        theSession.Store(new Target { NullableDateTime = DateTime.MinValue });
        theSession.Store(new Target { NullableDateTime = null });

        await theSession.SaveChangesAsync();

        theSession.Query<Target>().Where(x => !x.NullableDateTime.HasValue || x.NullableDateTime > new DateTime(2525,1,1)).Count()
            .ShouldBe(4);
    }

    [Fact]
    public async Task query_against_null_6()
    {
        theSession.Store(new Target { NullableBoolean = null });
        theSession.Store(new Target { NullableBoolean = true });

        await theSession.SaveChangesAsync();

        theSession.Query<Target>().Count(x => x.NullableBoolean.HasValue == false)
            .ShouldBe(1);
    }

    [Fact]
    public async Task query_against_not_null()
    {
        theSession.Store(new Target { NullableNumber = 3 });
        theSession.Store(new Target { NullableNumber = null });
        theSession.Store(new Target { NullableNumber = null });
        theSession.Store(new Target { NullableNumber = 3 });
        theSession.Store(new Target { NullableNumber = null });

        await theSession.SaveChangesAsync();

        theSession.Query<Target>().Count(x => x.NullableNumber.HasValue)
            .ShouldBe(2);
    }

    public nullable_types(DefaultStoreFixture fixture, ITestOutputHelper output) : base(fixture)
    {
        _output = output;
    }
}
