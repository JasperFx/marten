using System;
using System.Linq;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Xunit;

namespace DocumentDbTests.Reading.Linq
{
    public class query_against_date_Tests : OneOffConfigurationsContext
    {
        [Fact]
        public void query()
        {
            theSession.Store(new Target{Number = 1, Date = DateTime.UtcNow.AddMinutes(30)});
            theSession.Store(new Target{Number = 2, Date = DateTime.UtcNow.AddDays(1)});
            theSession.Store(new Target{Number = 3, Date = DateTime.UtcNow.AddHours(1)});
            theSession.Store(new Target{Number = 4, Date = DateTime.UtcNow.AddHours(-2)});
            theSession.Store(new Target{Number = 5, Date = DateTime.UtcNow.AddHours(-3)});

            theSession.SaveChanges();

            theSession.Query<Target>()
                .Where(x => x.Date > DateTime.UtcNow)
                .ToArray()
                .Select(x => x.Number)
                .ShouldHaveTheSameElementsAs(1, 2, 3);
        }

        [Fact]
        public void can_index_against_datetime_offset()
        {
            StoreOptions(_ =>
            {
                _.Schema.For<Target>().Index(x => x.Date);
            });

            theSession.Store(new Target { Number = 1, Date = DateTime.UtcNow.AddMinutes(30) });
            theSession.Store(new Target { Number = 2, Date = DateTime.UtcNow.AddDays(1) });
            theSession.Store(new Target { Number = 3, Date = DateTime.UtcNow.AddHours(1) });
            theSession.Store(new Target { Number = 4, Date = DateTime.UtcNow.AddHours(-2) });
            theSession.Store(new Target { Number = 5, Date = DateTime.UtcNow.AddHours(-3) });

            theSession.SaveChanges();


            theSession.Query<Target>()
                .Where(x => x.Date > DateTime.UtcNow)
                .OrderBy(x => x.DateOffset)
                .ToArray()
                .Select(x => x.Number)
                .ShouldHaveTheSameElementsAs(1, 3, 2);
        }

    }
}
