using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Marten.Linq;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class compiled_query_problem_with_nested_properties : IntegratedFixture
    {
        [Fact]
        public void can_do_a_compiled_query_on_nested_property()
        {
            theStore.BulkInsert(Target.GenerateRandomData(100).ToArray());

            using (var session = theStore.QuerySession())
            {
                var list = session.Query(new CompiledNestedQuery {Number = 5}).ToList();
                list.ShouldNotBeNull();
            }
        }
    }

    public class CompiledNestedQuery : ICompiledListQuery<Target>
    {
        Expression<Func<IQueryable<Target>, IEnumerable<Target>>> ICompiledQuery<Target, IEnumerable<Target>>.QueryIs()
        {
            return q => q.Where(x => x.Inner.Number == Number);
        }

        public int Number { get; set; }
    }
}