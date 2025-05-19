using System;
using System.Linq;
using System.Threading.Tasks;
using Marten.Exceptions;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;

namespace LinqTests.Bugs;

public class Bug_2198_querying_against_UTC_DateTime_with_Npgsql : BugIntegrationContext
{
    [Fact]
    public async Task query()
    {
        theSession.Store(new Target{Number = 1, Date = DateTime.UtcNow.AddMinutes(30)});
        theSession.Store(new Target{Number = 2, Date = DateTime.UtcNow.AddDays(1)});
        theSession.Store(new Target{Number = 3, Date = DateTime.UtcNow.AddHours(1)});
        theSession.Store(new Target{Number = 4, Date = DateTime.UtcNow.AddHours(-2)});
        theSession.Store(new Target{Number = 5, Date = DateTime.UtcNow.AddHours(-3)});

        await theSession.SaveChangesAsync();

        Should.Throw<InvalidUtcDateTimeUsageException>(() =>
        {
            theSession.Query<Target>()
                .Where(x => x.Date > DateTime.UtcNow)
                .ToArray()
                .Select(x => x.Number)
                .ShouldHaveTheSameElementsAs(1, 2, 3);
        });
    }

    [Fact]
    public async Task can_index_against_datetime_offset()
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

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


        theSession.Query<Target>()
            .Where(x => x.DateOffset > DateTimeOffset.UtcNow)
            .OrderBy(x => x.DateOffset)
            .ToArray()
            .Select(x => x.Number)
            .ShouldHaveTheSameElementsAs(1, 3, 2);
    }

}
