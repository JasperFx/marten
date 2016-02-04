using System.Linq;
using Marten.Schema.Hierarchies;
using Shouldly;
using Xunit;

namespace Marten.Testing.Schema.Hierarchies
{
    public class HierarchyArgumentTests
    {
        private HierarchyMapping mapping;
        private HierarchyArgument arg;

        public HierarchyArgumentTests()
        {
            mapping = new HierarchyMapping(typeof(Squad), new StoreOptions());

            arg = new HierarchyArgument(mapping);
        }

        [Fact]
        public void can_write_its_argument()
        {
            arg.ToCtorArgument().ShouldBe("Marten.Schema.Hierarchies.HierarchyMapping hierarchy");
        }

        [Fact]
        public void resolves_the_mapping()
        {
            arg.GetValue(null).ShouldBe(mapping);
        }
    }

    public class HierarchyMappingTests
    {
        [Fact]
        public void to_arguments_adds_a_hierarchy_argument_for_itself()
        {
            var mapping = new HierarchyMapping(typeof(Squad), new StoreOptions());

            mapping.ToArguments().OfType<HierarchyArgument>()
                .Single().Mapping.ShouldBeSameAs(mapping);
        }
    }
}