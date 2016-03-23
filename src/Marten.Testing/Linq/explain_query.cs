using System;
using System.Linq;
using Marten.Linq;
using Marten.Services;

namespace Marten.Testing.Linq
{
    public class explain_query : DocumentSessionFixture<NulloIdentityMap>
    {
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

            var plan = theSession.Query<SimpleUser>().Where(u=>u.Number > 5).Explain();
            plan.ShouldNotBeNull();
            plan.PlanWidth.ShouldBeGreaterThan(0);
            plan.PlanRows.ShouldBeGreaterThan(0);
            plan.TotalCost.ShouldBeGreaterThan(0m);
        }
    }
}