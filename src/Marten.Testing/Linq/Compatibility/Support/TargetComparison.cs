using System;
using System.Linq;
using System.Threading.Tasks;
using Marten.Testing.Documents;
using Marten.Testing.Harness;

namespace Marten.Testing.Linq.Compatibility.Support
{
    public class TargetComparison: LinqTestCase
    {
        private readonly Func<IQueryable<Target>, IQueryable<Target>> _func;

        public TargetComparison(Func<IQueryable<Target>, IQueryable<Target>> func)
        {
            _func = func;
        }

        public override async Task Compare(IQuerySession session, Target[] documents)
        {
            var expected = _func(documents.AsQueryable()).Select(x => x.Id).ToArray();

            var actual = (await (_func(session.Query<Target>()).Select(x => x.Id).ToListAsync())).ToArray();

            assertSame(expected, actual);
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
    }
}
