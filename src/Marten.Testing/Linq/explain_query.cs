using System;
using System.Linq;
using Marten.Services;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.Linq
{
    public class explain_query: IntegrationContext
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
            SpecificationExtensions.ShouldNotBeNull(plan);
            SpecificationExtensions.ShouldBeGreaterThan(plan.PlanWidth, 0);
            SpecificationExtensions.ShouldBeGreaterThan(plan.PlanRows, 0);
            SpecificationExtensions.ShouldBeGreaterThan(plan.TotalCost, 0m);
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
            SpecificationExtensions.ShouldNotBeNull(plan);
            SpecificationExtensions.ShouldBeGreaterThan(plan.PlanWidth, 0);
            SpecificationExtensions.ShouldBeGreaterThan(plan.PlanRows, 0);
            SpecificationExtensions.ShouldBeGreaterThan(plan.TotalCost, 0m);
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
            SpecificationExtensions.ShouldNotBeNull(plan);
            SpecificationExtensions.ShouldBeGreaterThan(plan.ActualTotalTime, 0m);
            SpecificationExtensions.ShouldBeGreaterThan(plan.PlanningTime, 0m);
            SpecificationExtensions.ShouldBeGreaterThan(plan.ExecutionTime, 0m);
            plan.SortKey.ShouldContain("(((d.data ->> 'Number'::text))::integer)");
            plan.Plans.ShouldNotBeEmpty();
        }

        public explain_query(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }
}
