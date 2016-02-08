using System;
using System.Linq;
using Marten.Schema;
using Marten.Schema.Hierarchies;
using Shouldly;
using Xunit;

namespace Marten.Testing.Schema.Hierarchies
{
    public class HierarchyArgumentTests
    {
        private DocumentMapping mapping;
        private HierarchyArgument arg;

        public HierarchyArgumentTests()
        {
            mapping = new DocumentMapping(typeof(Squad), new StoreOptions());

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
        public void alias_for_base_type()
        {
            mapping.AliasFor(typeof(Squad)).ShouldBe(DocumentMapping.BaseAlias);
        }

        [Fact]
        public void type_for_alias_hit()
        {
            mapping.TypeFor("football").ShouldBe(typeof (FootballTeam));
            mapping.TypeFor("baseball_team").ShouldBe(typeof (BaseballTeam));
        }

        [Fact]
        public void type_for_BASE()
        {
            mapping.TypeFor("BASE").ShouldBe(typeof(Squad));
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
            arg.ToCtorArgument().ShouldBe("Marten.Schema.DocumentMapping hierarchy");
        }

        [Fact]
        public void resolves_the_mapping()
        {
            arg.GetValue(null).ShouldBe(mapping);
        }
    }


}