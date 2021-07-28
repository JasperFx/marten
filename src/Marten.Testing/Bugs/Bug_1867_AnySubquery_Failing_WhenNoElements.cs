using Marten.Testing.Harness;
using Shouldly;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug_1867_AnySubquery_FailingOnInMemoryEnumerable: BugIntegrationContext
    {
        public class MyData
        {
            public Guid Id { get; set; }
        }

        [Fact]
        public async Task try_to_query_through_list_and_do_not_blow_up()
        {
            var matchingGuid = Guid.NewGuid();
            var externalData = new List<Guid>() { matchingGuid };

            var data = new MyData { Id = matchingGuid };
            theSession.Store(data);
            await theSession.SaveChangesAsync();

            var q1 = await theSession.Query<MyData>().Where(d => externalData.Any(e => e == d.Id))
                .FirstOrDefaultAsync();

            q1.Id.ShouldBe(matchingGuid);
        }
    }
}
