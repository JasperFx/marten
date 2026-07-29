using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Services.Json;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;

namespace LinqTests.Bugs;

public class Bug_1189_can_select_transform_without_an_id : IntegrationContext
{
    public Bug_1189_can_select_transform_without_an_id(DefaultStoreFixture fixture) : base(fixture)
    {
    }

    public class TargetView
    {
        public Colors Color { get; set; }
        public int Number { get; set; }
    }


    [Fact]
    public async Task can_select()
    {
        var targets = Target.GenerateRandomData(100).ToArray();
        await theStore.BulkInsertAsync(targets);

        // Was FirstOrDefault(); Marten 9.0 made data access async-only. This test was
        // invisible to the v2 runner (see the SerializerTypeTargetedFact notes), so it was
        // never updated when that change landed.
        var view = await theSession.Query<Target>()
            .Select(x => new TargetView { Color = x.Color, Number = x.Number })
            .FirstOrDefaultAsync();

        view.ShouldNotBeNull();
    }
}
