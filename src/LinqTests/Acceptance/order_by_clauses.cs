using System.Linq;
using LinqTests.Acceptance.Support;

namespace LinqTests.Acceptance;

public class order_by_clauses: LinqTestContext<order_by_clauses>
{
    public order_by_clauses(DefaultQueryFixture fixture) : base(fixture)
    {
    }

    static order_by_clauses()
    {
        ordered(t => t.OrderBy(x => x.String));
        ordered(t => t.OrderByDescending(x => x.String));

        ordered(t => t.OrderBy(x => x.Number).ThenBy(x => x.String));
        ordered(t => t.OrderBy(x => x.Number).ThenByDescending(x => x.String));
        ordered(t => t.OrderByDescending(x => x.Number).ThenBy(x => x.String));

        ordered(t => t.OrderBy(x => x.String).Take(2));
        ordered(t => t.OrderBy(x => x.String).Skip(2));
        ordered(t => t.OrderBy(x => x.String).Take(2).Skip(2));
    }

}
