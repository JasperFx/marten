using System;
using System.Linq;
using System.Threading.Tasks;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
namespace LinqTests.Acceptance;

public class nested_boolean_logic : IntegrationContext
{
    [Fact]
    public async Task TestModalOrQuery()
    {
        var target1 = new Target { String = "Bert", Date = new DateTime(2016, 03, 10) };
        var target2 = new Target { String = null, Date = new DateTime(2016, 03, 10) };

        theSession.Store(target1, target2);
        await theSession.SaveChangesAsync();

        var startDate = new DateTime(2016, 03, 01);
        var endDate = new DateTime(2016, 04, 01);

        var query = theSession.Query<Target>().Where(item => (item.String != null && item.Date >= startDate && item.Date <= endDate)
                                                             || (item.String == null && item.Date >= startDate && item.Date <= endDate));

        query.ToList().Count.ShouldBeGreaterThanOrEqualTo(2);

    }

    public nested_boolean_logic(DefaultStoreFixture fixture) : base(fixture)
    {
    }
}
