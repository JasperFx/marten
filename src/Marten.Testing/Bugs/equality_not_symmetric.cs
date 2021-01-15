using System;
using System.Linq;
using System.Threading.Tasks;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.Bugs
{
    public sealed class equality_not_symmetric: IntegrationContext
    {
        public equality_not_symmetric(DefaultStoreFixture fixture) : base(fixture)
        {
        }
        
        [Fact]
        public void equality_equals_operator_should_be_symmetric()
        {
            var random = Target.Random();
            var theString = random.String;
            using (var session = theStore.OpenSession())
            {
                session.Insert(random);
                session.SaveChanges();
            }

            using (var session = theStore.QuerySession())
            {

                session.Query<Target>()
                    .Where(x => x.String == (theString))
                    .ToList()
                    .Count
                    .ShouldBe(1);
                
                session.Query<Target>()
                    .Where(x => theString == x.String )
                    .ToList()
                    .Count
                    .ShouldBe(1);
            }
        }

        [Fact]
        public async Task equality_equals_should_be_symmetric()
        {
            var random = Target.Random();
            var theString = random.String;
            using (var session = theStore.OpenSession())
            {
                session.Insert(random);
                session.SaveChanges();
            }

            using (var session = theStore.QuerySession())
            {

                session.Query<Target>()
                    .Where(x => x.String.Equals(theString))
                    .ToList()
                    .Count
                    .ShouldBe(1);
                
                session.Query<Target>()
                    .Where(x => theString.Equals(x.String))
                    .ToList()
                    .Count
                    .ShouldBe(1);
            }
        }
        
        [Fact]
        public async Task equality_equals_ignoring_case_should_be_symmetric()
        {
            var random = Target.Random();
            var theString = random.String;
            using (var session = theStore.OpenSession())
            {
                session.Insert(random);
                session.SaveChanges();
            }

            using (var session = theStore.QuerySession())
            {
                
                session.Query<Target>()
                    .Where(x => x.String.Equals(theString, StringComparison.InvariantCultureIgnoreCase))
                    .ToList()
                    .Count
                    .ShouldBe(1);

                session.Query<Target>()
                    .Where(x => theString.Equals(x.String, StringComparison.InvariantCultureIgnoreCase))
                    .ToList()
                    .Count
                    .ShouldBe(1);
                
            }
        }
    }
}