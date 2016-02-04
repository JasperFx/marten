using System;
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

            mapping.AddSubClass(typeof(BasketballTeam));
            mapping.AddSubClass(typeof(BaseballTeam));
            mapping.AddSubClass(typeof(FootballTeam), "football");
        }

        [Fact]
        public void alias_for_hit()
        {
            mapping.AliasFor(typeof(BasketballTeam)).ShouldBe("basketball_team");
            mapping.AliasFor(typeof(FootballTeam)).ShouldBe("football");
        }

        [Fact]
        public void alias_for_miss()
        {
            Exception<ArgumentOutOfRangeException>.ShouldBeThrownBy(() =>
            {
                mapping.AliasFor(GetType());
            });
        }

        [Fact]
        public void type_for_alias_hit()
        {
            mapping.TypeFor("football").ShouldBe(typeof (FootballTeam));
            mapping.TypeFor("baseball_team").ShouldBe(typeof (BaseballTeam));
        }

        [Fact]
        public void type_for_alias_miss()
        {
            Exception<ArgumentOutOfRangeException>.ShouldBeThrownBy(() =>
            {
                mapping.TypeFor("cricket_team");
            });
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