using System;
using System.Linq;
using Marten.Testing.Fixtures;

namespace Marten.Testing.Linq
{
    public class query_against_dateoffset_Tests : DocumentSessionFixture
    {
        public void query()
        {
            theSession.Store(new Target{Number = 1, DateOffset = DateTimeOffset.Now.AddMinutes(5)});
            theSession.Store(new Target{Number = 2, DateOffset = DateTimeOffset.Now.AddDays(1)});
            theSession.Store(new Target{Number = 3, DateOffset = DateTimeOffset.Now.AddHours(1)});
            theSession.Store(new Target{Number = 4, DateOffset = DateTimeOffset.Now.AddHours(-2)});
            theSession.Store(new Target{Number = 5, DateOffset = DateTimeOffset.Now.AddHours(-3)});

            theSession.SaveChanges();

            theSession.Query<Target>().Where(x => x.DateOffset > DateTimeOffset.Now).ToArray()
                .Select(x => x.Number)
                .ShouldHaveTheSameElementsAs(1, 2, 3);
        }
    }
}