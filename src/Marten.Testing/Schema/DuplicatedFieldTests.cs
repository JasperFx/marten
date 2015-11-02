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
            column.Name.ShouldBe("FirstName");
            column.Type.ShouldBe("varchar");
        }

    }
}