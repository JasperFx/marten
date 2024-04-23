using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Schema;
using Marten.Testing.Harness;
using Shouldly;
using Xunit.Abstractions;

namespace LinqTests.Bugs;

public class Bug_3151_using_duplicated_fields_in_linq_queries : BugIntegrationContext
{
    private readonly ITestOutputHelper _output;

    public Bug_3151_using_duplicated_fields_in_linq_queries(ITestOutputHelper output)
    {
        _output = output;
    }


    [Fact]
    public async Task filtering_by_null_ids_works()
    {
        var p1 = new Person { ChildId = Guid.NewGuid()};
        var p2 = new Person { ChildId = Guid.NewGuid()};
        theSession.Store(p1,p2);
        await theSession.SaveChangesAsync();
        Guid?[] ids = [p1.ChildId];
        var results = await theSession.Query<Person>()
            .Where(x => x.ChildId.In(ids))
            .ToListAsync();
        results.Single().ChildId.ShouldBe(p1.ChildId);
    }

    [Fact]
    public async Task filtering_by_non_null_ids_using_value_fails_regression_from_641()
    {
        var p1 = new Person { ChildId = Guid.NewGuid()};
        var p2 = new Person { ChildId = Guid.NewGuid()};
        theSession.Store(p1,p2);
        await theSession.SaveChangesAsync();
        Guid[] ids = [p1.ChildId.Value];
        var results = await theSession.Query<Person>()
            .Where(x => x.ChildId!.Value.In(ids))
            .ToListAsync();
        results.Single().ChildId.ShouldBe(p1.ChildId);
    }

    [Fact]
    public async Task Bug_3150_query_multiple_times()
    {
        // if you call BeginTransactionAsync then the issue goes away..
        //await theSession.BeginTransactionAsync(CancellationToken.None);

        async Task DoQuery()
        {
            var children = new Dictionary<Guid, Person3150>();
            await theSession.Query<Person3150>()
                .Include(x => x.ChildId!, children)
                .ToListAsync();
        }

        theSession.Logger = new TestOutputMartenLogger(_output);

        await DoQuery();

        // second invocation throws MartenCommandException "42P01: relation "mt_temp_id_list1" does not exist"
        await DoQuery();
    }
}

public class Person
{
    public Guid Id { get; set; }

    [DuplicateField]
    public Guid? ChildId { get; set; }
}

public class Person3150
{
    public Guid Id { get; set; }

    [DuplicateField]
    public string? Name { get; set; }
    public Guid? ChildId { get; set; }
}
