using System.Linq;
using System.Reflection;
using Marten.Schema;
using Marten.Testing.Documents;
using Marten.Testing.Fixtures;
using Shouldly;

namespace Marten.Testing.Schema
{
    public class LateralJoinFieldTests
    {
        public readonly LateralJoinField theStringField = LateralJoinField.For<User>(x => x.FirstName);
        public readonly LateralJoinField theNumberField = LateralJoinField.For<User>(x => x.Age);
        public readonly LateralJoinField theEnumField = LateralJoinField.For<Target>(x => x.Color);

        public void member_name_is_derived()
        {
            theStringField.MemberName.ShouldBe("FirstName");
        }

        public void has_the_member_path()
        {
            theStringField.Members.Single().ShouldBeAssignableTo<PropertyInfo>()
                .Name.ShouldBe("FirstName");
        }

        public void locator_for_string()
        {
            theStringField.SqlLocator.ShouldBe("l.\"FirstName\"");
        }

        public void locator_for_number()
        {
            theNumberField.SqlLocator.ShouldBe("l.\"Age\"");
        }

        public void lateral_join_declaration_for_string()
        {
            theStringField.LateralJoinDeclaration.ShouldBe("\"FirstName\" varchar");
        }

        public void lateral_join_declaration_for_number()
        {
            theNumberField.LateralJoinDeclaration.ShouldBe("\"Age\" integer");
        }

        public void lateral_join_declaration_for_enum()
        {
            theEnumField.LateralJoinDeclaration.ShouldBe("\"Color\" integer");
        }

    }
}