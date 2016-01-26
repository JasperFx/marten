using Marten.Generation;
using Marten.Schema;
using Marten.Testing.Documents;
using Shouldly;
using Xunit;

namespace Marten.Testing.Generation
{
    public class TableDefinitionTests
    {
        [Fact]
        public void equivalency_positive()
        {
            var users = new DocumentMapping(typeof(User));
            var table1 = users.ToTable(null);
            var table2 = users.ToTable(null);

            table2.ShouldBe(table1);
            table1.ShouldBe(table2);
            table1.ShouldNotBeSameAs(table2);
        }

        [Fact]
        public void equivalency_negative_different_numbers_of_columns()
        {
            var users = new DocumentMapping(typeof(User));
            var table1 = users.ToTable(null);
            var table2 = users.ToTable(null);

            table2.Columns.Add(new TableColumn("user_name", "character varying"));

            table2.ShouldNotBe(table1);
        }

        [Fact]
        public void equivalency_negative_column_type_changed()
        {
            var users = new DocumentMapping(typeof(User));
            var table1 = users.ToTable(null);
            var table2 = users.ToTable(null);

            table2.PrimaryKey.Type = "int";

            table2.ShouldNotBe(table1);
        }
    }
}