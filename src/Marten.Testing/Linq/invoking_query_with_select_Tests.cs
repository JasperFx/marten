using System;
using System.Linq;
using System.Threading.Tasks;
using Marten.Services;
using Marten.Testing.Documents;
using Marten.Util;
using Shouldly;
using Xunit;

namespace Marten.Testing.Linq
{
    public class invoking_query_with_select_Tests : DocumentSessionFixture<NulloIdentityMap>
    {
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


    }

    public class UserName
    {
        public string Name { get; set; }
    }
}