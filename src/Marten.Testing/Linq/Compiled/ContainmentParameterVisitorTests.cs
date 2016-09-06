using System;
using System.Linq;
using System.Linq.Expressions;
using Marten.Linq;
using Xunit;

namespace Marten.Testing.Linq.Compiled
{
    public class ContainmentParameterVisitorTests
    {
        [Fact]
        public void can_build_up_the_containment_parameter_simple_case()
        {
            var expression = new SimpleQuery();
        }

        public class SimpleQuery : ICompiledQuery<Target>
        {
            public string Name { get; set; }

            public Expression<Func<IQueryable<Target>, Target>> QueryIs()
            {
                return q => q.FirstOrDefault(x => x.Children.Any(c => c.String == Name));
            }
        }
    }
}