using System.Linq;
using Marten.Testing.Linq.Compatibility.Support;

namespace Marten.Testing.Linq.Compatibility
{
    public class simple_order_by_clauses: LinqTestContext<DefaultQueryFixture, simple_order_by_clauses>
    {
        public simple_order_by_clauses(DefaultQueryFixture fixture) : base(fixture)
        {
        }

        static simple_order_by_clauses()
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
}
