using System.Reflection;
using FubuCore.Reflection;
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
    }
}