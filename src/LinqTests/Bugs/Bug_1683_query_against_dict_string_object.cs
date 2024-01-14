using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using Xunit.Abstractions;

namespace LinqTests.Bugs;

public class Bug_1683_query_against_dict_string_object : BugIntegrationContext
{
    private readonly ITestOutputHelper _output;

    public Bug_1683_query_against_dict_string_object(ITestOutputHelper output)
    {
        _output = output;
    }

    public class MyData
    {
        public Dictionary<string, Object> Data { get; set; } = new Dictionary<string, object>();
        public Guid Id { get; set; }
    }

    [Fact]
    public async Task try_to_query_through_dictionary_and_do_not_blow_up()
    {
        var data1 = new MyData {Data = new Dictionary<string, object> {{"hello", 1}}};
        var data2 = new MyData {Data = new Dictionary<string, object> {{"hello", 7}}};

        TheSession.Store(data1, data2);
        await TheSession.SaveChangesAsync();

        TheSession.Logger = new TestOutputMartenLogger(_output);

        var q1 = await TheSession.Query<MyData>().Where(p => p.Data["hello"] == (object)7)
            .FirstOrDefaultAsync();

        q1.Id.ShouldBe(data2.Id);


    }
}
