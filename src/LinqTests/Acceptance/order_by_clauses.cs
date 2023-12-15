using System.Linq;
using System.Threading.Tasks;
using LinqTests.Acceptance.Support;
using Xunit.Abstractions;

namespace LinqTests.Acceptance;

public class order_by_clauses: LinqTestContext<order_by_clauses>
{
    public order_by_clauses(DefaultQueryFixture fixture, ITestOutputHelper output) : base(fixture)
    {
        TestOutput = output;
    }

    static order_by_clauses()
    {
        ordered(t => t.OrderBy(x => x.String).ThenBy(x => x.Id));
        ordered(t => t.OrderByDescending(x => x.String).ThenBy(x => x.Id));

        ordered(t => t.OrderBy(x => x.Number).ThenBy(x => x.String).ThenBy(x => x.Id).Take(10));
        ordered(t => t.OrderBy(x => x.Number).ThenByDescending(x => x.String).ThenBy(x => x.Id).Take(10));
        ordered(t => t.OrderByDescending(x => x.Number).ThenBy(x => x.String).ThenBy(x => x.Id).Take(10));

        ordered(t => t.OrderByDescending(x => x.Inner.Number));
    }


    [Theory]
    [MemberData(nameof(GetDescriptions))]
    public Task run_query(string description)
    {
        return assertTestCase(description, Fixture.Store);
    }

    [Theory]
    [MemberData(nameof(GetDescriptions))]
    public Task with_duplicated_fields(string description)
    {
        return assertTestCase(description, Fixture.DuplicatedFieldStore);
    }

}
