using Shouldly;
using Xunit;

namespace Marten.Testing
{
    public class DdlRulesTester
    {
        [Fact]
        public void default_grant_roles_is_empty()
        {
            new DdlRules().Grants.ShouldBeEmpty();
        }


        [Fact]
        public void role_is_null_by_default()
        {
            new DdlRules().Role.ShouldBeNull();
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