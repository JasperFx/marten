using System;
using System.Linq;
using System.Threading.Tasks;
using Marten.PLv8.Patching;
using Marten.Schema;
using Marten.Storage;
using Marten.Testing.CoreFunctionality;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;
using Xunit;
using Xunit.Abstractions;

namespace Marten.PLv8.Testing.Patching
{
    public class MultiTenancyFixture: StoreFixture
    {
        public MultiTenancyFixture(): base("multi_tenancy")
        {
            Options.Policies.AllDocumentsAreMultiTenanted();
            Options.Schema.For<User>().UseOptimisticConcurrency(true);
            Options.UseJavascriptTransformsAndPatching();
        }
    }

    [Collection("multi_tenancy")]
    public class multi_tenancy: StoreContext<MultiTenancyFixture>, IClassFixture<MultiTenancyFixture>
    {
        private readonly ITestOutputHelper _output;
        private readonly Target[] _greens = Target.GenerateRandomData(100).ToArray();

        private readonly Target[] _reds = Target.GenerateRandomData(100).ToArray();
        private readonly Target[] blues = Target.GenerateRandomData(25).ToArray();
        private readonly Target targetBlue1 = Target.Random();
        private readonly Target targetBlue2 = Target.Random();
        private readonly Target targetRed1 = Target.Random();
        private readonly Target targetRed2 = Target.Random();

        public multi_tenancy(MultiTenancyFixture fixture, ITestOutputHelper output): base(fixture)
        {
            _output = output;
            using (var session = theStore.OpenSession("Red"))
            {
                session.Store(targetRed1, targetRed2);
                session.SaveChanges();
            }

            using (var session = theStore.OpenSession("Blue"))
            {
                session.Store(targetBlue1, targetBlue2);
                session.SaveChanges();
            }
        }


        [Fact]
        public void patching_respects_tenancy_too()
        {
            var user = new User {UserName = "Me", FirstName = "Jeremy", LastName = "Miller"};
            user.Id = Guid.NewGuid();

            using (var red = theStore.OpenSession("Red"))
            {
                red.Store(user);
                red.SaveChanges();
            }

            using (var green = theStore.OpenSession("Green"))
            {
                green.Patch<User>(user.Id).Set(x => x.FirstName, "John");
                green.SaveChanges();
            }

            using (var red = theStore.QuerySession("Red"))
            {
                var final = red.Load<User>(user.Id);
                final.FirstName.ShouldBe("Jeremy");
            }
        }

        [Fact]
        public void patching_respects_tenancy_too_2()
        {
            var user = new User {UserName = "Me", FirstName = "Jeremy", LastName = "Miller"};
            user.Id = Guid.NewGuid();

            using (var red = theStore.OpenSession("Red"))
            {
                red.Store(user);
                red.SaveChanges();
            }

            using (var green = theStore.OpenSession("Green"))
            {
                green.Patch<User>(x => x.UserName == "Me").Set(x => x.FirstName, "John");
                green.SaveChanges();
            }

            using (var red = theStore.QuerySession("Red"))
            {
                var final = red.Load<User>(user.Id);
                final.FirstName.ShouldBe("Jeremy");
            }
        }



        [MultiTenanted]
        public class TenantedDoc
        {
            public Guid Id;
        }


    }
}
