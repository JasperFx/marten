using System;
using System.Linq;
using System.Threading.Tasks;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.Bugs
{
    public sealed class Bug_1703_Equality_Not_Symmetric: IntegrationContext
    {
        public Bug_1703_Equality_Not_Symmetric(DefaultStoreFixture fixture) : base(fixture)
        {
        }
        
        [Fact]
        public void string_equality_equals_operator_should_be_symmetric()
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
        public async Task string_equality_equals_should_be_symmetric()
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
        public async Task string_equality_equals_ignoring_case_should_be_symmetric()
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
        
        [Fact]
        public async Task object_equality_equals_should_be_symmetric()
        {
            var random = Target.Random();
            var theNumber = random.Number;
            using (var session = theStore.OpenSession())
            {
                session.Insert(random);
                session.SaveChanges();
            }

            using (var session = theStore.QuerySession())
            {

                session.Query<Target>()
                    .Where(x => x.Number.Equals(theNumber))
                    .ToList()
                    .Count
                    .ShouldBe(1);
                
                session.Query<Target>()
                    .Where(x => theNumber.Equals(x.Number))
                    .ToList()
                    .Count
                    .ShouldBe(1);
            }
        }
        
        [Fact]
        public async Task object_equality_equals_operator_should_be_symmetric()
        {
            var random = Target.Random();
            var theNumber = random.Number;
            using (var session = theStore.OpenSession())
            {
                session.Insert(random);
                session.SaveChanges();
            }

            using (var session = theStore.QuerySession())
            {

                session.Query<Target>()
                    .Where(x => x.Number == theNumber )
                    .ToList()
                    .Count
                    .ShouldBe(1);
                
                session.Query<Target>()
                    .Where(x => theNumber == x.Number)
                    .ToList()
                    .Count
                    .ShouldBe(1);
            }
        }
    }
}