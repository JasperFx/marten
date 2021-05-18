using System.Linq;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.CoreFunctionality
{
    public class select_json : IntegrationContext
    {
        public select_json(DefaultStoreFixture fixture) : base(fixture)
        {
        }

        [Fact]
        public void use_select_to_anonymous_type_with_first_as_json()
        {
            theSession.Store(new User { FirstName = "Hank" });
            theSession.Store(new User { FirstName = "Bill" });
            theSession.Store(new User { FirstName = "Sam" });
            theSession.Store(new User { FirstName = "Tom" });

            theSession.SaveChanges();

            #region sample_AsJson-plus-Select-2
            theSession
                .Query<User>()
                .OrderBy(x => x.FirstName)

                // Transform to an anonymous type
                .Select(x => new { Name = x.FirstName })

                // Select only the raw JSON
                .AsJson()
                .FirstOrDefault()
                .ShouldBe("{\"Name\": \"Bill\"}");
            #endregion sample_AsJson-plus-Select-2
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

            #region sample_AsJson-plus-Select-1
            theSession
                .Query<User>()
                .OrderBy(x => x.FirstName)

                // Transform the User class to a different type
                .Select(x => new UserName { Name = x.FirstName })
                .AsJson()
                .First()
                .ShouldBe("{\"Name\": \"Bill\"}");
            #endregion sample_AsJson-plus-Select-1
        }

        public class UserName
        {
            public string Name { get; set; }
        }


        [Fact]
        public void select_many_with_select_and_as_json()
        {
            var targets = Target.GenerateRandomData(100).ToArray();
            theStore.BulkInsert(targets);

            using (var query = theStore.QuerySession())
            {
                var actual = query.Query<Target>()
                    .SelectMany(x => x.Children)
                    .Where(x => x.Color == Colors.Green)
                    .Select(x => new { Id = x.Id, Shade = x.Color })
                    .AsJson()
                    .ToList();

                var expected = targets
                    .SelectMany(x => x.Children).Count(x => x.Color == Colors.Green);

                actual.Count.ShouldBe(expected);
            }
        }



    }
}
