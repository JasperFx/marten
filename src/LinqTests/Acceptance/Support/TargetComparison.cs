using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;

namespace LinqTests.Acceptance.Support;

public class TargetComparison: LinqTestCase
{
    private readonly Func<IQueryable<Target>, IQueryable<Target>> _func;
    private bool _no_CTE_Usage_Allowed;

    public TargetComparison(Func<IQueryable<Target>, IQueryable<Target>> func)
    {
        _func = func;
    }

    public override async Task Compare(IQuerySession session, Target[] documents,
        TestOutputMartenLogger logger)
    {
        var expected = _func(documents.AsQueryable()).Select(x => x.Id).ToArray();

        var actual = (await (_func(session.Query<Target>()).Select(x => x.Id).ToListAsync())).ToArray();

        assertSame(expected, actual);


        if (_no_CTE_Usage_Allowed)
        {
            var sql = logger.ExecutedSql();
            if (sql.Contains("WITH"))
            {
                throw new Exception("This query should not be using CTE:\n" + sql);
            }
        }

    }

    private void assertSame(Guid[] expected, Guid[] actual)
    {
        if (!Ordered)
        {
            expected = expected.OrderBy(x => x).ToArray();
            actual = actual.OrderBy(x => x).ToArray();
        }

        actual.ShouldHaveTheSameElementsAs(expected);
    }

    public void NoCteUsage()
    {
        _no_CTE_Usage_Allowed = true;
    }
}
