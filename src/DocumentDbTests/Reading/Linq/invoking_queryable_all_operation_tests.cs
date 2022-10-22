using System.Linq;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Reading.Linq;

public class invoking_queryable_all_operation_tests: IntegrationContext
{
    public invoking_queryable_all_operation_tests(DefaultStoreFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public void invoking_queryable_all_operation_test1()
    {
        theSession.Store(new User { FirstName = "Hank" , Roles = new []{ "R1", default(string)}});
        theSession.Store(new User { FirstName = "Bill" , Roles = new []{ "R3", "R5"} });
        theSession.Store(new User { FirstName = "Sam" , Roles = new []{ "R1", "R2"} });
        theSession.Store(new User { FirstName = "Tom" , Roles = new []{ "R3", "R5"} });
        theSession.Store(new User { FirstName = "Joe" , Roles = new []{ "R1", "R1"} });
        theSession.Store(new User { FirstName = "Tom" , Roles = new []{ "R1", "R1"} });
        theSession.Store(new User { FirstName = "Joe" , Roles = new []{ default(string), default(string)} });
        theSession.SaveChanges();

        var results = theSession.Query<User>()
            .Where(u => u.Roles.All(r => r == "R1"))
            .ToList();

        results.Count.ShouldBe(2);
    }

    [Fact]
    public void invoking_queryable_all_operation_test2()
    {
        theSession.Store(new User { FirstName = "Hank" , Roles = new []{ "R1", default(string)}});
        theSession.Store(new User { FirstName = "Bill" , Roles = new []{ "R3", "R5"} });
        theSession.Store(new User { FirstName = "Sam" , Roles = new []{ "R1", "R2"} });
        theSession.Store(new User { FirstName = "Tom" , Roles = new []{ "R3", "R5"} });
        theSession.Store(new User { FirstName = "Joe" , Roles = new []{ "R1", "R1"} });
        theSession.Store(new User { FirstName = "Tom" , Roles = new []{ "R1", "R1"} });
        theSession.Store(new User { FirstName = "Joe" , Roles = new []{ default(string), default(string)} });
        theSession.SaveChanges();

        var results = theSession.Query<User>()
            .Where(u => u.FirstName == "Joe" && u.Roles.All(r => r == "R1"))
            .ToList();

        results.Count.ShouldBe(1);
    }

    [Fact]
    public void invoking_queryable_all_operation_test3()
    {
        theSession.Store(new User { FirstName = "Hank" , Roles = new []{ "R1", default(string)}});
        theSession.Store(new User { FirstName = "Bill" , Roles = new []{ "R3", "R5"} });
        theSession.Store(new User { FirstName = "Sam" , Roles = new []{ "R1", "R2"} });
        theSession.Store(new User { FirstName = "Tom" , Roles = new []{ "R3", "R5"} });
        theSession.Store(new User { FirstName = "Joe" , Roles = new []{ "R1", "R1"} });
        theSession.Store(new User { FirstName = "Tom" , Roles = new []{ "R1", "R1"} });
        theSession.Store(new User { FirstName = "Joe" , Roles = new []{ default(string), default(string)} });
        theSession.SaveChanges();

        var results = theSession.Query<User>()
            .Where(u => u.FirstName == "Jean" && u.Roles.All(r => r == "R1"))
            .ToList();

        results.Count.ShouldBe(0);
    }

    [Fact]
    public void invoking_queryable_all_operation_test4()
    {
        theSession.Store(new User { FirstName = "Hank" , Roles = new []{ "R1", default(string)}});
        theSession.Store(new User { FirstName = "Bill" , Roles = new []{ "R3", "R5"} });
        theSession.Store(new User { FirstName = "Sam" , Roles = new []{ "R1", "R2"} });
        theSession.Store(new User { FirstName = "Tom" , Roles = new []{ "R3", "R5"} });
        theSession.Store(new User { FirstName = "Joe" , Roles = new []{ "R1", "R1"} });
        theSession.Store(new User { FirstName = "Tom" , Roles = new []{ "R1", "R1"} });
        theSession.Store(new User { FirstName = "Joe" , Roles = new []{ default(string), default(string)} });
        theSession.SaveChanges();

        var results = theSession.Query<User>()
            .Where(u => u.Roles.All(r => r == null))
            .ToList();

        results.Count.ShouldBe(1);
    }
}
