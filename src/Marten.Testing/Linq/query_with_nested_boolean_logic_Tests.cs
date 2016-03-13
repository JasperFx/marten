using System;
using System.Collections.Generic;
using System.Linq;
using Marten.Services;
using Marten.Testing.Fixtures;
using Shouldly;
using Xunit;

namespace Marten.Testing.Linq
{
    public class query_with_nested_boolean_logic_Tests : DocumentSessionFixture<NulloIdentityMap>
    {
        [Fact]
        public void TestModalOrQuery()
        {
            var target1 = new Target { String = "Bert", Date = new DateTime(2016, 03, 10) };
            var target2 = new Target { String = null, Date = new DateTime(2016, 03, 10) };

            theSession.Store(target1, target2);
            theSession.SaveChanges();

            var startDate = new DateTime(2016, 03, 01);
            var endDate = new DateTime(2016, 04, 01);

            var query = theSession.Query<Target>().Where(item => (item.String != null && item.Date >= startDate && item.Date <= endDate)
                || (item.String == null && item.Date >= startDate && item.Date <= endDate));

            query.ToList().Count.ShouldBeGreaterThanOrEqualTo(2);

        }
    }
}