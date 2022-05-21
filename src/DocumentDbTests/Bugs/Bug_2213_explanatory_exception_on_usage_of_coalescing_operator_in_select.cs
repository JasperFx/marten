using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Exceptions;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Bugs
{
    public class Bug_2213_explanatory_exception_on_usage_of_coalescing_operator_in_select : BugIntegrationContext
    {
        [Fact]
        public async Task throw_nice_message()
        {
            var ex = await Should.ThrowAsync<BadLinqExpressionException>(async () =>
            {
                var targets = await theSession.Query<Parent>()
                    .OrderBy(x => (x.First ?? x.Second).Name).ToListAsync();
            });

            ex.Message.ShouldBe($"Invalid OrderBy() expression '([x].First ?? [x].Second).Name'");


        }
    }

    public class Parent
    {
        public Guid Id { get; set; }
        public Child First { get; set; }
        public Child Second { get; set; }
    }

    public class Child
    {
        public string Name { get; set; }
    }


}
