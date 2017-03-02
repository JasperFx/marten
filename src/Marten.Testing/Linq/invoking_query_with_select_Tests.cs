using System;
using System.Linq;
using System.Threading.Tasks;
using Baseline;
using Marten.Services;
using Marten.Testing.Documents;
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

            var names = await theSession.Query<User>().OrderBy(x => x.FirstName).Select(x => x.FirstName).ToListAsync().ConfigureAwait(false);
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

        [Fact]
        public void use_select_to_another_type_as_json()
        {
            theSession.Store(new User { FirstName = "Hank" });
            theSession.Store(new User { FirstName = "Bill" });
            theSession.Store(new User { FirstName = "Sam" });
            theSession.Store(new User { FirstName = "Tom" });

            theSession.SaveChanges();

            // Postgres sticks some extra spaces into the JSON string

            // SAMPLE: AsJson-plus-Select-1
            theSession
                .Query<User>()
                .OrderBy(x => x.FirstName)
                
                // Transform the User class to a different type
                .Select(x => new UserName { Name = x.FirstName })
                .AsJson()
                .First()
                .ShouldBe("{\"Name\": \"Bill\"}");
            // ENDSAMPLE
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
        public void use_select_to_anonymous_type_with_first_as_json()
        {
            theSession.Store(new User { FirstName = "Hank" });
            theSession.Store(new User { FirstName = "Bill" });
            theSession.Store(new User { FirstName = "Sam" });
            theSession.Store(new User { FirstName = "Tom" });

            theSession.SaveChanges();


            // SAMPLE:AsJson-plus-Select-2
            theSession
                .Query<User>()
                .OrderBy(x => x.FirstName)
                
                // Transform to an anonymous type
                .Select(x => new { Name = x.FirstName })

                // Select only the raw JSON
                .AsJson()
                .FirstOrDefault()
                .ShouldBe("{\"Name\": \"Bill\"}");
            // ENDSAMPLE
        }

        [Fact]
        public void use_select_to_anonymous_type_with_to_json_array()
        {
            theSession.Store(new User { FirstName = "Hank" });
            theSession.Store(new User { FirstName = "Bill" });
            theSession.Store(new User { FirstName = "Sam" });
            theSession.Store(new User { FirstName = "Tom" });

            theSession.SaveChanges();

            theSession.Query<User>().OrderBy(x => x.FirstName).Select(x => new { Name = x.FirstName })
                .ToJsonArray()
                .ShouldBe("[{\"Name\": \"Bill\"},{\"Name\": \"Hank\"},{\"Name\": \"Sam\"},{\"Name\": \"Tom\"}]");
        }


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
                .ToListAsync().ConfigureAwait(false);


            users.Select(x => x.Name)
                .ShouldHaveTheSameElementsAs("Bill", "Hank", "Sam", "Tom");
        }

        [Fact]
        public void use_select_to_another_type_as_to_json_array()
        {
            theSession.Store(new User { FirstName = "Hank" });
            theSession.Store(new User { FirstName = "Bill" });
            theSession.Store(new User { FirstName = "Sam" });
            theSession.Store(new User { FirstName = "Tom" });

            theSession.SaveChanges();

            var users = theSession
                .Query<User>()
                .OrderBy(x => x.FirstName)
                .Select(x => new UserName { Name = x.FirstName })
                .ToJsonArray();

            users.ShouldBe("[{\"Name\": \"Bill\"},{\"Name\": \"Hank\"},{\"Name\": \"Sam\"},{\"Name\": \"Tom\"}]");
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
        public void use_select_with_multiple_fields_to_other_type_using_constructor()
        {
            theSession.Store(new User { FirstName = "Hank", LastName = "Aaron" });
            theSession.Store(new User { FirstName = "Bill", LastName = "Laimbeer" });
            theSession.Store(new User { FirstName = "Sam", LastName = "Mitchell" });
            theSession.Store(new User { FirstName = "Tom", LastName = "Chambers" });

            theSession.SaveChanges();

            var users = theSession.Query<User>()
                .Select(x => new UserDto(x.FirstName, x.LastName))
                .ToList();

            users.Count.ShouldBe(4);

            users.Each(x =>
            {
                x.FirstName.ShouldNotBeNull();
                x.LastName.ShouldNotBeNull();
            });
        }

        [Fact]
        public void use_select_with_multiple_fields_to_other_type_using_constructor_and_properties()
        {
            theSession.Store(new User { FirstName = "Hank", LastName = "Aaron", Age = 20 });
            theSession.Store(new User { FirstName = "Bill", LastName = "Laimbeer", Age = 40 });
            theSession.Store(new User { FirstName = "Sam", LastName = "Mitchell", Age = 60 });
            theSession.Store(new User { FirstName = "Tom", LastName = "Chambers", Age = 80 });

            theSession.SaveChanges();

            var users = theSession.Query<User>()
                .Select(x => new UserDto(x.FirstName, x.LastName) {YearsOld = x.Age})
                .ToList();

            users.Count.ShouldBe(4);

            users.Each(x =>
            {
                x.FirstName.ShouldNotBeNull();
                x.LastName.ShouldNotBeNull();
                x.YearsOld.ShouldBeGreaterThan(0);
            });
        }

        public class UserDto
        {
            public UserDto(string firstName, string lastName)
            {
                FirstName = firstName;
                LastName = lastName;
            }

            public string FirstName { get; }
            public string LastName { get; }
            public int YearsOld { get; set; }
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
                .ToListAsync().ConfigureAwait(false);

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

        [Fact]
        public void transform_with_deep_properties_to_anonymous_type()
        {
            var target = Target.Random(true);

            theSession.Store(target);
            theSession.SaveChanges();

            var actual = theSession.Query<Target>()
                .Where(x => x.Id == target.Id)
                .Select(x => new {x.Id, x.Number, InnerNumber = x.Inner.Number})
                .First();

            actual.Id.ShouldBe(target.Id);
            actual.Number.ShouldBe(target.Number);
            actual.InnerNumber.ShouldBe(target.Inner.Number);
        }

        [Fact]
        public void transform_with_deep_properties_to_type_using_constructor()
        {
            var target = Target.Random(true);

            theSession.Store(target);
            theSession.SaveChanges();

            var actual = theSession.Query<Target>()
                .Where(x => x.Id == target.Id)
                .Select(x => new FlatTarget(x.Id, x.Number, x.Inner.Number))
                .First();

            actual.Id.ShouldBe(target.Id);
            actual.Number.ShouldBe(target.Number);
            actual.InnerNumber.ShouldBe(target.Inner.Number);
        }

        public class FlatTarget
        {
            public FlatTarget(Guid id, int number, int innerNumber)
            {
                Id = id;
                Number = number;
                InnerNumber = innerNumber;
            }

            public Guid Id { get; }
            public int Number { get; }
            public int InnerNumber { get; }
        }
    }

    public class UserName
    {
        public string Name { get; set; }
    }
}
