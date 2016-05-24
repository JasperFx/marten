using System.Linq;
using Marten.Schema;
using Marten.Schema.Arguments;
using Marten.Testing.Documents;
using Shouldly;
using Xunit;

namespace Marten.Testing.Schema
{
    // UpsertFunction is mostly tested through integration tests
    public class UpsertFunctionTests
    {
        [Fact]
        public void add_the_optimistic_version_arg_if_mapping_has_that()
        {
            var mapping = DocumentMapping.For<Issue>();
            mapping.UseOptimisticConcurrency = true;

            var func = new UpsertFunction(mapping);

            func.OrderedArguments().OfType<CurrentVersionArgument>().Any()
                .ShouldBeTrue();
        }


        [Fact]
        public void no_current_version_argument_if_not_configured_on_the_mapping()
        {
            var mapping = DocumentMapping.For<Issue>();
            mapping.UseOptimisticConcurrency = false;

            var func = new UpsertFunction(mapping);

            func.OrderedArguments().OfType<CurrentVersionArgument>().Any()
                .ShouldBeFalse();
        }
    }
}