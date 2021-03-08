using System;
using System.Collections.Generic;
using System.Linq;
using Marten.Testing.Harness;
using Xunit;
using System.Threading.Tasks;
using Shouldly;

namespace Marten.Testing.Bugs
{
    public class Bug_1683_query_against_dict_string_object : BugIntegrationContext
    {
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

            theSession.Store(data1, data2);
            await theSession.SaveChangesAsync();

            var q1 = await theSession.Query<MyData>().Where(p => p.Data["hello"] == (object)7)
                .FirstOrDefaultAsync();

            q1.Id.ShouldBe(data2.Id);


        }
    }
}
