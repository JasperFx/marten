using System;
using System.Linq;
using System.Threading.Tasks;
using Marten.Testing.Documents;
using Marten.Testing.Harness;

namespace LinqTests.Acceptance;

public class date_type_usage : OneOffConfigurationsContext
{
    [Fact]
    public async Task query()
    {
        theSession.Store(new Target{Number = 1, DateOffset = DateTimeOffset.UtcNow.AddMinutes(30)});
        theSession.Store(new Target{Number = 2, DateOffset = DateTimeOffset.UtcNow.AddDays(1)});
        theSession.Store(new Target{Number = 3, DateOffset = DateTimeOffset.UtcNow.AddHours(1)});
        theSession.Store(new Target{Number = 4, DateOffset = DateTimeOffset.UtcNow.AddHours(-2)});
        theSession.Store(new Target{Number = 5, DateOffset = DateTimeOffset.UtcNow.AddHours(-3)});

        await theSession.SaveChangesAsync();

        theSession.Query<Target>().Where(x => x.DateOffset > DateTimeOffset.UtcNow).ToArray()
            .Select(x => x.Number)
            .ShouldHaveTheSameElementsAs(1, 2, 3);
    }

    [Fact]
    public async Task can_index_against_datetime_offset()
    {
        StoreOptions(_ =>
        {
            _.Schema.For<Target>().Index(x => x.DateOffset);
        });

        theSession.Store(new Target { Number = 1, DateOffset = DateTimeOffset.UtcNow.AddMinutes(30) });
        theSession.Store(new Target { Number = 2, DateOffset = DateTimeOffset.UtcNow.AddDays(1) });
        theSession.Store(new Target { Number = 3, DateOffset = DateTimeOffset.UtcNow.AddHours(1) });
        theSession.Store(new Target { Number = 4, DateOffset = DateTimeOffset.UtcNow.AddHours(-2) });
        theSession.Store(new Target { Number = 5, DateOffset = DateTimeOffset.UtcNow.AddHours(-3) });

        await theSession.SaveChangesAsync();


        theSession.Query<Target>().Where(x => x.DateOffset > DateTimeOffset.UtcNow).OrderBy(x => x.DateOffset).ToArray()
            .Select(x => x.Number)
            .ShouldHaveTheSameElementsAs(1, 3, 2);
    }

    [Fact]
    public async Task can_select_DateTimeOffset_and_will_return_localtime()
    {
        var document = Target.Random();
        document.DateOffset = DateTimeOffset.UtcNow;

        using (var session = theStore.LightweightSession())
        {
            session.Insert(document);
            await session.SaveChangesAsync();
        }

        using (var query = theStore.QuerySession())
        {
            var dateOffset = query.Query<Target>().Where(x => x.Id == document.Id).Select(x => x.DateOffset).Single();

            // be aware of the Npgsql DateTime mapping https://www.npgsql.org/doc/types/datetime.html
            dateOffset.ShouldBeEqualWithDbPrecision(document.DateOffset.ToLocalTime());
        }
    }

}
