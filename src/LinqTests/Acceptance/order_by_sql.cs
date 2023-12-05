using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Xunit.Abstractions;

namespace LinqTests.Acceptance;

public class order_by_sql : OneOffConfigurationsContext
{
    private readonly ITestOutputHelper _output;

    public order_by_sql(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task sort_by_literal_sql()
    {
        StoreOptions(x =>
        {
            x.Schema.For<Target>()
                .Duplicate(x => x.String)
                .Duplicate(x => x.AnotherString);
        });

        var targets = Target.GenerateRandomData(100).ToArray();
        await theStore.BulkInsertAsync(targets);

        theSession.Logger = new TestOutputMartenLogger(_output);

        var expected = await theSession
            .Query<Target>()
            .OrderBy(x => x.String)
            .ThenByDescending(x => x.AnotherString)
            .Select(x => x.Id)
            .ToListAsync();

        var command = theSession
            .Query<Target>()
            .OrderBySql("string")
            .ThenBySql("another_string desc")
            .Select(x => x.Id).ToCommand();

        _output.WriteLine(command.CommandText);

        var actual = await theSession
            .Query<Target>()
            .OrderBySql("string")
            .ThenBySql("another_string desc")
            .Select(x => x.Id)
            .ToListAsync();

        actual.ShouldHaveTheSameElementsAs(expected);
    }
}
