using System;
using System.Linq;
using System.Threading.Tasks;
using Baseline;
using Marten.Services;
using Marten.Testing.Documents;
using Marten.Testing.Fixtures;
using Marten.Util;
using Shouldly;
using Xunit;

namespace Marten.Testing.Linq
{
    public class invoking_query_with_select_Tests : DocumentSessionFixture<NulloIdentityMap>
    {
        // SAMPLE: one_field_projection
        [Fact]
        public void use_select_in_query_for_one_field()
        {
            theSession.Store(new User {FirstName = "Hank"});
            theSession.Store(new User {FirstName = "Bill"});
            theSession.Store(new User {FirstName = "Sam"});
            theSession.Store(new User {FirstName = "Tom"});

            theSession.SaveChanges();

            theSession.Query<User>().OrderBy(x => x.FirstName).Select(x => x.FirstName)
                .ShouldHaveTheSameElementsAs("Bill", "Hank", "Sam", "Tom");

        }
        // ENDSAMPLE

        [Fact]
        public void use_select_in_query_for_one_field_and_first()
        {
            theSession.Store(new User { FirstName = "Hank" });
            theSession.Store(new User { FirstName = "Bill" });
            theSession.Store(new User { FirstName = "Sam" });
            theSession.Store(new User { FirstName = "Tom" });

            theSession.SaveChanges();

            theSession.Query<User>().OrderBy(x => x.FirstName).Select(x => x.FirstName)
                .First().ShouldBe("Bill");

        }

        [Fact]
        public async Task use_select_in_query_for_one_field_async()
        {
            theSession.Store(new User { FirstName = "Hank" });
            theSession.Store(new User { FirstName = "Bill" });
            theSession.Store(new User { FirstName = "Sam" });
            theSession.Store(new User { FirstName = "Tom" });

            theSession.SaveChanges();

            var names = await theSession.Query<User>().OrderBy(x => x.FirstName).Select(x => x.FirstName).ToListAsync();
            names.ShouldHaveTheSameElementsAs("Bill", "Hank", "Sam", "Tom");

        }

        [Fact]
        public void use_select_to_another_type()
        {
            theSession.Store(new User { FirstName = "Hank" });
            theSession.Store(new User { FirstName = "Bill" });
            theSession.Store(new User { FirstName = "Sam" });
            theSession.Store(new User { FirstName = "Tom" });

            theSession.SaveChanges();

            theSession.Query<User>().OrderBy(x => x.FirstName).Select(x => new UserName {Name = x.FirstName})
                .ToArray()
                .Select(x => x.Name)
                .ShouldHaveTheSameElementsAs("Bill", "Hank", "Sam", "Tom");
        }

        // SAMPLE: get_first_projection
        [Fact]
        public void use_select_to_another_type_with_first()
        {
            theSession.Store(new User { FirstName = "Hank" });
            theSession.Store(new User { FirstName = "Bill" });
            theSession.Store(new User { FirstName = "Sam" });
            theSession.Store(new User { FirstName = "Tom" });

            theSession.SaveChanges();

            theSession.Query<User>().OrderBy(x => x.FirstName).Select(x => new UserName { Name = x.FirstName })
                .FirstOrDefault()
                .Name.ShouldBe("Bill");
        }
        // ENDSAMPLE


        [Fact]
        public async Task use_select_to_another_type_async()
        {
            theSession.Store(new User { FirstName = "Hank" });
            theSession.Store(new User { FirstName = "Bill" });
            theSession.Store(new User { FirstName = "Sam" });
            theSession.Store(new User { FirstName = "Tom" });

            theSession.SaveChanges();

            var users = await theSession
                .Query<User>()
                .OrderBy(x => x.FirstName)
                .Select(x => new UserName { Name = x.FirstName })
                .ToListAsync();


            users.Select(x => x.Name)
                .ShouldHaveTheSameElementsAs("Bill", "Hank", "Sam", "Tom");
        }
        
        // SAMPLE: anonymous_type_projection
        [Fact]
        public void use_select_to_transform_to_an_anonymous_type()
        {
            theSession.Store(new User { FirstName = "Hank" });
            theSession.Store(new User { FirstName = "Bill" });
            theSession.Store(new User { FirstName = "Sam" });
            theSession.Store(new User { FirstName = "Tom" });

            theSession.SaveChanges();

            theSession.Query<User>().OrderBy(x => x.FirstName).Select(x => new { Name = x.FirstName })
                .ToArray()
                .Select(x => x.Name)
                .ShouldHaveTheSameElementsAs("Bill", "Hank", "Sam", "Tom");
        }
        // ENDSAMPLE

        [Fact]
        public void use_select_with_multiple_fields_in_anonymous()
        {
            theSession.Store(new User { FirstName = "Hank", LastName = "Aaron"});
            theSession.Store(new User { FirstName = "Bill", LastName = "Laimbeer"});
            theSession.Store(new User { FirstName = "Sam", LastName = "Mitchell"});
            theSession.Store(new User { FirstName = "Tom", LastName = "Chambers"});

            theSession.SaveChanges();

            var users = theSession.Query<User>().Select(x => new {First = x.FirstName, Last = x.LastName}).ToList();

            users.Count.ShouldBe(4);

            users.Each(x =>
            {
                x.First.ShouldNotBeNull();
                x.Last.ShouldNotBeNull();
            });
        }

        // SAMPLE: other_type_projection
        [Fact]
        public void use_select_with_multiple_fields_to_other_type()
        {
            theSession.Store(new User { FirstName = "Hank", LastName = "Aaron" });
            theSession.Store(new User { FirstName = "Bill", LastName = "Laimbeer" });
            theSession.Store(new User { FirstName = "Sam", LastName = "Mitchell" });
            theSession.Store(new User { FirstName = "Tom", LastName = "Chambers" });

            theSession.SaveChanges();

            var users = theSession.Query<User>().Select(x => new User2{ First = x.FirstName, Last = x.LastName }).ToList();

            users.Count.ShouldBe(4);

            users.Each(x =>
            {
                x.First.ShouldNotBeNull();
                x.Last.ShouldNotBeNull();
            });
        }
        // ENDSAMPLE

        public class User2
        {
            public string First;
            public string Last;
        }


        [Fact]
        public async Task use_select_to_transform_to_an_anonymous_type_async()
        {
            theSession.Store(new User { FirstName = "Hank" });
            theSession.Store(new User { FirstName = "Bill" });
            theSession.Store(new User { FirstName = "Sam" });
            theSession.Store(new User { FirstName = "Tom" });

            theSession.SaveChanges();

            var users = await theSession
                .Query<User>()
                .OrderBy(x => x.FirstName)
                .Select(x => new { Name = x.FirstName })
                .ToListAsync();

            users
                .Select(x => x.Name)
                .ShouldHaveTheSameElementsAs("Bill", "Hank", "Sam", "Tom");
        }

        // SAMPLE: deep_properties_projection
        [Fact]
        public void transform_with_deep_properties()
        {
            var targets = Target.GenerateRandomData(100).ToArray();

            theStore.BulkInsert(targets);

            var actual = theSession.Query<Target>().Where(x => x.Number == targets[0].Number).Select(x => x.Inner.Number).ToList().Distinct();

            var expected = targets.Where(x => x.Number == targets[0].Number).Select(x => x.Inner.Number).Distinct();

            actual.ShouldHaveTheSameElementsAs(expected);
        }
        // ENDSAMPLE

    }

    public class UserName
    {
        public string Name { get; set; }
    }
}