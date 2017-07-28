using System;
using System.Linq;
using Marten.Services;
using Marten.Util;
using Shouldly;
using Xunit;

namespace Marten.Testing.Linq
{
    public class explain_query : DocumentSessionFixture<NulloIdentityMap>
    {
        [Fact]
        public void retrieves_query_plan()
        {
            var user1 = new SimpleUser
            {
                UserName = "Mr Fouine",
                Number = 5,
                Birthdate = new DateTime(1986, 10, 4),
                Address = new Address { HouseNumber = "12bis", Street = "rue de la martre" }
            };
            var user2 = new SimpleUser
            {
                UserName = "Mrs Fouine",
                Number = 6,
                Birthdate = new DateTime(1987, 10, 4),
                Address = new Address { HouseNumber = "12bis", Street = "rue de la martre" }
            };
            theSession.Store(user1, user2);
            theSession.SaveChanges();

            var plan = theSession.Query<SimpleUser>().Explain();
            plan.ShouldNotBeNull();
            plan.PlanWidth.ShouldBeGreaterThan(0);
            plan.PlanRows.ShouldBeGreaterThan(0);
            plan.TotalCost.ShouldBeGreaterThan(0m);
        }

        [Fact]
        public void retrieves_query_plan_with_where()
        {
            var user1 = new SimpleUser
            {
                UserName = "Mr Fouine",
                Number = 5,
                Birthdate = new DateTime(1986, 10, 4),
                Address = new Address { HouseNumber = "12bis", Street = "rue de la martre" }
            };
            var user2 = new SimpleUser
            {
                UserName = "Mrs Fouine",
                Number = 6,
                Birthdate = new DateTime(1987, 10, 4),
                Address = new Address { HouseNumber = "12bis", Street = "rue de la martre" }
            };
            theSession.Store(user1, user2);
            theSession.SaveChanges();

            var plan = theSession.Query<SimpleUser>().Where(u => u.Number > 5).Explain();
            plan.ShouldNotBeNull();
            plan.PlanWidth.ShouldBeGreaterThan(0);
            plan.PlanRows.ShouldBeGreaterThan(0);
            plan.TotalCost.ShouldBeGreaterThan(0m);
        }

        [Fact]
        public void retrieves_query_plan_with_where_and_all_options_enabled()
        {
            var user1 = new SimpleUser
            {
                UserName = "Mr Fouine",
                Number = 5,
                Birthdate = new DateTime(1986, 10, 4),
                Address = new Address { HouseNumber = "12bis", Street = "rue de la martre" }
            };
            var user2 = new SimpleUser
            {
                UserName = "Mrs Fouine",
                Number = 6,
                Birthdate = new DateTime(1987, 10, 4),
                Address = new Address { HouseNumber = "12bis", Street = "rue de la martre" }
            };
            theSession.Store(user1, user2);
            theSession.SaveChanges();

            var plan = theSession.Query<SimpleUser>().Where(u => u.Number > 5)
                .OrderBy(x => x.Number)
                .Explain(c =>
            {
                c
                .Analyze()
                .Buffers()
                .Costs()
                .Timing()
                .Verbose();
            });
            plan.ShouldNotBeNull();
            plan.ActualTotalTime.ShouldBeGreaterThan(0m);
            plan.PlanningTime.ShouldBeGreaterThan(0m);
            plan.ExecutionTime.ShouldBeGreaterThan(0m);
            plan.SortKey.ShouldContain($"(((d.data ->> '{theSession.ColumnName<SimpleUser>(u => u.Number)}'::text))::integer)");
            plan.Plans.ShouldNotBeEmpty();
        }
    }
}