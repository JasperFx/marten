using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit.Abstractions;

namespace LinqTests.Operators;

public class all_operator: IntegrationContext
{
    public all_operator(DefaultStoreFixture fixture, ITestOutputHelper output) : base(fixture)
    {
        theSession.Logger = new TestOutputMartenLogger(output);
    }

    [Fact]
    public async Task invoking_queryable_all_operation_test1()
    {
        theSession.Store(new User { FirstName = "Hank" , Roles = new []{ "R1", default(string)}});
        theSession.Store(new User { FirstName = "Bill" , Roles = new []{ "R3", "R5"} });
        theSession.Store(new User { FirstName = "Sam" , Roles = new []{ "R1", "R2"} });
        theSession.Store(new User { FirstName = "Tom" , Roles = new []{ "R3", "R5"} });
        theSession.Store(new User { FirstName = "Joe" , Roles = new []{ "R1", "R1"} });
        theSession.Store(new User { FirstName = "Tom" , Roles = new []{ "R1", "R1"} });
        theSession.Store(new User { FirstName = "Joe" , Roles = new []{ default(string), default(string)} });
        await theSession.SaveChangesAsync();

        var results = theSession.Query<User>()
            .Where(u => u.Roles.All(r => r == "R1"))
            .ToList();

        results.Count.ShouldBe(2);
    }

    [Fact]
    public async Task invoking_queryable_all_operation_test2()
    {
        theSession.Store(new User { FirstName = "Hank" , Roles = new []{ "R1", default(string)}});
        theSession.Store(new User { FirstName = "Bill" , Roles = new []{ "R3", "R5"} });
        theSession.Store(new User { FirstName = "Sam" , Roles = new []{ "R1", "R2"} });
        theSession.Store(new User { FirstName = "Tom" , Roles = new []{ "R3", "R5"} });
        theSession.Store(new User { FirstName = "Joe" , Roles = new []{ "R1", "R1"} });
        theSession.Store(new User { FirstName = "Tom" , Roles = new []{ "R1", "R1"} });
        theSession.Store(new User { FirstName = "Joe" , Roles = new []{ default(string), default(string)} });
        await theSession.SaveChangesAsync();

        var results = theSession.Query<User>()
            .Where(u => u.FirstName == "Joe" && u.Roles.All(r => r == "R1"))
            .ToList();

        results.Count.ShouldBe(1);
    }

    [Fact]
    public async Task invoking_queryable_all_operation_test3()
    {
        theSession.Store(new User { FirstName = "Hank" , Roles = new []{ "R1", default(string)}});
        theSession.Store(new User { FirstName = "Bill" , Roles = new []{ "R3", "R5"} });
        theSession.Store(new User { FirstName = "Sam" , Roles = new []{ "R1", "R2"} });
        theSession.Store(new User { FirstName = "Tom" , Roles = new []{ "R3", "R5"} });
        theSession.Store(new User { FirstName = "Joe" , Roles = new []{ "R1", "R1"} });
        theSession.Store(new User { FirstName = "Tom" , Roles = new []{ "R1", "R1"} });
        theSession.Store(new User { FirstName = "Joe" , Roles = new []{ default(string), default(string)} });
        await theSession.SaveChangesAsync();

        var results = theSession.Query<User>()
            .Where(u => u.FirstName == "Jean" && u.Roles.All(r => r == "R1"))
            .ToList();

        results.Count.ShouldBe(0);
    }

    [Fact]
    public async Task invoking_queryable_all_operation_test4()
    {
        theSession.Store(new User { FirstName = "Hank" , Roles = new []{ "R1", default(string)}});
        theSession.Store(new User { FirstName = "Bill" , Roles = new []{ "R3", "R5"} });
        theSession.Store(new User { FirstName = "Sam" , Roles = new []{ "R1", "R2"} });
        theSession.Store(new User { FirstName = "Tom" , Roles = new []{ "R3", "R5"} });
        theSession.Store(new User { FirstName = "Joe" , Roles = new []{ "R1", "R1"} });
        theSession.Store(new User { FirstName = "Tom" , Roles = new []{ "R1", "R1"} });
        theSession.Store(new User { FirstName = "Joe" , Roles = new []{ default(string), default(string)} });
        await theSession.SaveChangesAsync();

        /* SHOULD BE
WITH mt_temp_id_list1CTE as (
select ctid, CAST(ARRAY(SELECT jsonb_array_elements_text(CAST(d.data ->> 'Roles' as jsonb))) as varchar[]) as data from public.mt_doc_user as d
)
  , mt_temp_id_list2CTE as (
select ctid, data from mt_temp_id_list1CTE as d where  true = ALL (select unnest(data) is null)
)
 select d.id, d.data from public.mt_doc_user as d where d.ctid in (select ctid from mt_temp_id_list2CTE)



 ACTUAL:
 select d.id, d.data from public.mt_doc_user as d where :p0 = ALL(CAST(ARRAY(SELECT jsonb_array_elements_text(CAST(d.data ->> 'Roles' as jsonb))) as varchar[]));
  p0:

         */

        var results = theSession.Query<User>()
            .Where(u => u.Roles.All(r => r == null))
            .ToList();

        results.Count.ShouldBe(1);
    }

    [Fact]
    public async Task invoking_queryable_all_operation_test5()
    {
        theSession.Store(new User
        {
            FirstName = "Joe" , Roles = new []{ "R1", default(string)},
            Friends = new List<Friend> { new(){ Name = "F1"}, new(){ Name = "F1"}}
        });
        theSession.Store(new User
        {
            FirstName = "Bill" , Roles = new []{ "R1", default(string)},
            Friends = new List<Friend> { new(){ Name = "F1"}, new(){ Name = "F1"}}
        });
        theSession.Store(new User
        {
            FirstName = "Joe" , Roles = new []{ "R1", default(string)},
            Friends = new List<Friend> { new(){ Name = "F1"}, new(){ Name = "F2"}}
        });
        theSession.Store(new User
        {
            FirstName = "Joe" , Roles = new []{ "R1", default(string)},
            Friends = new List<Friend> { new(){ Name = default}, new(){ Name = default}}
        });
        await theSession.SaveChangesAsync();

        var results = theSession.Query<User>()
            .Where(u => u.Friends.All(f => f.Name == "F1"))
            .ToList();

        results.Count.ShouldBe(2);
    }

    [Fact]
    public async Task invoking_queryable_all_operation_test6()
    {
        theSession.Store(new User
        {
            FirstName = "Joe" , Roles = new []{ "R1", default(string)},
            Friends = new List<Friend> { new(){ Name = "F1"}, new(){ Name = "F1"}}
        });
        theSession.Store(new User
        {
            FirstName = "Bill" , Roles = new []{ "R1", default(string)},
            Friends = new List<Friend> { new(){ Name = "F1"}, new(){ Name = "F1"}}
        });
        theSession.Store(new User
        {
            FirstName = "Joe" , Roles = new []{ "R1", default(string)},
            Friends = new List<Friend> { new(){ Name = "F1"}, new(){ Name = "F2"}}
        });
        theSession.Store(new User
        {
            FirstName = "Joe" , Roles = new []{ "R1", default(string)},
            Friends = new List<Friend> { new(){ Name = default}, new(){ Name = default}}
        });
        await theSession.SaveChangesAsync();

        var results = theSession.Query<User>()
            .Where(u => u.FirstName == "Joe" && u.Friends.All(f => f.Name == "F1"))
            .ToList();

        results.Count.ShouldBe(1);
    }

    [Fact]
    public async Task invoking_queryable_all_operation_test7()
    {
        theSession.Store(new User
        {
            FirstName = "Joe" , Roles = new []{ "R1", default(string)},
            Friends = new List<Friend> { new(){ Name = "F1"}, new(){ Name = "F1"}}
        });
        theSession.Store(new User
        {
            FirstName = "Bill" , Roles = new []{ "R1", default(string)},
            Friends = new List<Friend> { new(){ Name = "F1"}, new(){ Name = "F1"}}
        });
        theSession.Store(new User
        {
            FirstName = "Joe" , Roles = new []{ "R1", default(string)},
            Friends = new List<Friend> { new(){ Name = "F1"}, new(){ Name = "F2"}}
        });
        theSession.Store(new User
        {
            FirstName = "Joe" , Roles = new []{ "R1", default(string)},
            Friends = new List<Friend> { new(){ Name = default}, new(){ Name = default}}
        });
        await theSession.SaveChangesAsync();

        var results = theSession.Query<User>()
            .Where(u => u.Friends.All(f => f.Name == null))
            .ToList();

        results.Count.ShouldBe(1);
    }

    [Fact]
    public async Task invoking_queryable_all_operation_test8()
    {
        theSession.Store(new User
        {
            FirstName = "Joe" , Roles = new []{ "R1", default(string)},
            Friends = new List<Friend> { new(){ Name = "F1"}, new(){ Name = "F1"}}
        });
        theSession.Store(new User
        {
            FirstName = "Bill" , Roles = new []{ "R1", default(string)},
            Friends = new List<Friend> { new(){ Name = "F1"}, new(){ Name = "F1"}}
        });
        theSession.Store(new User
        {
            FirstName = "Joe" , Roles = new []{ "R1", default(string)},
            Friends = new List<Friend> { new(){ Name = "F1"}, new(){ Name = "F2"}}
        });
        theSession.Store(new User
        {
            FirstName = "Joe" , Roles = new []{ "R1", default(string)},
            Friends = new List<Friend> { new(){ Name = default}, new(){ Name = default}}
        });
        await theSession.SaveChangesAsync();

        var results = theSession.Query<User>()
            .Where(u => u.FirstName == "Bill" && u.Friends.All(f => f.Name == null))
            .ToList();

        results.Count.ShouldBe(0);
    }
}
