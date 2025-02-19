using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten.Testing.Harness;
using Shouldly;
using Xunit.Abstractions;

namespace LinqTests.Bugs;

public class Bug_834_querying_inside_of_child_collections : IntegrationContext
{
    private readonly ITestOutputHelper _output;

    public Bug_834_querying_inside_of_child_collections(DefaultStoreFixture fixture, ITestOutputHelper output) : base(fixture)
    {
        _output = output;
    }

    public class Contact {
        public List<string> Tags {get;set;}
        public Guid Id { get; set; }
    }

    [Fact]
    public async Task can_query_with_condition_within_any()
    {
        var c1 = new Contact{Tags = new List<string>{"AA", "B", "C"}};
        var c2 = new Contact{Tags = new List<string>{"B", "AC", "D"}};
        var c3 = new Contact{Tags = new List<string>{"H", "I", "J"}};

        await theStore.BulkInsertAsync(new Contact[]{c1, c2, c3});

        theSession.Logger = new TestOutputMartenLogger(_output);
        theSession.Query<Contact>().Where(x => x.Tags.Any(t => t.StartsWith("A"))).Any()
            .ShouldBeTrue();

        theSession.Query<Contact>()
            .Count(x => x.Tags.Any(t => t.StartsWith("A"))).ShouldBe(2);
    }
}
