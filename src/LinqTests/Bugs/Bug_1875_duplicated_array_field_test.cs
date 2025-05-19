using System;
using System.Linq;
using System.Threading.Tasks;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;

namespace LinqTests.Bugs;

public class Bug_1875_duplicated_array_field_test : BugIntegrationContext
{
    [Fact]
    public async Task query_on_duplicated_number_array_field_test()
    {
        StoreOptions(_ =>
        {
            _.Schema.For<Target>().Duplicate(t => t.NumberArray, "int[]");
        });

        using (var session = theStore.LightweightSession())
        {
            session.Store(new Target
            {
                NumberArray = new []{ 1, 2 }
            });

            await session.SaveChangesAsync();
        }

        using (var session = theStore.QuerySession())
        {
            session.Query<Target>().Single(x => x.NumberArray.Contains(1))
                .NumberArray[0].ShouldBe(1);
        }
    }

    [Fact]
    public async Task query_on_duplicated_guid_array_field_test()
    {
        StoreOptions(_ =>
        {
            _.Schema.For<Target>().Duplicate(t => t.GuidArray, "uuid[]");
        });

        var target = new Target {GuidArray = new Guid[] {Guid.NewGuid(), Guid.NewGuid()}};

        using (var session = theStore.LightweightSession())
        {
            session.Store(target);
            await session.SaveChangesAsync();
        }

        using (var session = theStore.QuerySession())
        {
            session.Query<Target>().Single(x => x.GuidArray.Contains(target.GuidArray[0]))
                .GuidArray[0].ShouldBe(target.GuidArray[0]);
        }
    }
}
