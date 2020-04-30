using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.CoreFunctionality
{
    public class DdlRulesTester
    {


        [Fact]
        public void role_is_null_by_default()
        {
            SpecificationExtensions.ShouldBeNull(new DdlRules().Role);
        }

        [Fact]
        public void table_creation_is_drop_then_create_by_default()
        {
            new DdlRules().TableCreation.ShouldBe(CreationStyle.DropThenCreate);
        }

        [Fact]
        public void upsert_rights_are_by_invoker_by_default()
        {
            new DdlRules().UpsertRights.ShouldBe(SecurityRights.Invoker);
        }
    }
}
