using System.Linq.Expressions;
using System.Reflection;
using Baseline.Reflection;
using Marten.Schema;
using Marten.Testing.Documents;
using NpgsqlTypes;
using Shouldly;
using Xunit;

namespace Marten.Testing.Schema
{
    public class DuplicatedFieldTests
    {
        private DuplicatedField theField = new DuplicatedField(EnumStorage.AsInteger, new MemberInfo[] { ReflectionHelper.GetProperty<User>(x => x.FirstName) });

        [Fact]
        public void default_role_is_search()
        {
            theField
                .Role.ShouldBe(DuplicatedFieldRole.Search);
        }

        [Fact]
        public void create_table_column_for_non_indexed_search()
        {
            var column = theField.ToColumn();
            column.Name.ShouldBe("first_name");
            column.Type.ShouldBe("varchar");
        }

        [Fact]
        public void upsert_argument_defaults()
        {
            theField.UpsertArgument.Arg.ShouldBe("arg_first_name");
            theField.UpsertArgument.Column.ShouldBe("first_name");
            theField.UpsertArgument.PostgresType.ShouldBe("varchar");
        }

        [Fact]
        public void sql_locator_with_default_column_name()
        {
            theField.SqlLocator.ShouldBe("d.first_name");
        }

        [Fact]
        public void sql_locator_with_custom_column_name()
        {
            theField.ColumnName = "x_first_name";
            theField.SqlLocator.ShouldBe("d.x_first_name");
        }

        [Fact]
        public void enum_field()
        {
            var field = DuplicatedField.For<Target>(EnumStorage.AsString, x => x.Color);
            field.UpsertArgument.DbType.ShouldBe(NpgsqlDbType.Varchar);
            field.UpsertArgument.PostgresType.ShouldBe("varchar");

            var constant = Expression.Constant((int)Colors.Blue);

            field.GetValue(constant).ShouldBe(Colors.Blue.ToString());
        }

        [Theory]
        [InlineData(EnumStorage.AsInteger, "color = (data ->> 'Color')::int")]
        [InlineData(EnumStorage.AsString, "color = data ->> 'Color'")]
        public void storage_is_set_when_passed_in(EnumStorage storageMode, string expectedUpdateFragment)
        {
            var field = DuplicatedField.For<Target>(storageMode, x => x.Color);
            field.UpdateSqlFragment().ShouldBe(expectedUpdateFragment);
        }
    }
}
