using System.Reflection;
using Baseline.Reflection;
using Marten.Schema;
using Marten.Testing.Documents;
using Shouldly;

namespace Marten.Testing.Schema
{
    public class DuplicatedFieldTests
    {
        private DuplicatedField theField = new DuplicatedField(new MemberInfo[] { ReflectionHelper.GetProperty<User>(x => x.FirstName)});

        public void default_role_is_search()
        {
            theField
                .Role.ShouldBe(DuplicatedFieldRole.Search);
        }

        public void create_table_column_for_non_indexed_search()
        {
            var column = theField.ToColumn(null);
            column.Name.ShouldBe("first_name");
            column.Type.ShouldBe("varchar");
        }

        public void upsert_argument_defaults()
        {
            theField.UpsertArgument.Arg.ShouldBe("arg_first_name");
            theField.UpsertArgument.Column.ShouldBe("first_name");
            theField.UpsertArgument.PostgresType.ShouldBe("varchar");
        }

        public void sql_locator_with_default_column_name()
        {
            theField.SqlLocator.ShouldBe("d.first_name");
        }

        public void sql_locator_with_custom_column_name()
        {
            theField.ColumnName = "x_first_name";
            theField.SqlLocator.ShouldBe("d.x_first_name");
        }

        public void lateral_join_is_always_null()
        {
            theField.LateralJoinDeclaration.ShouldBeNull();
        }
        

    }
}