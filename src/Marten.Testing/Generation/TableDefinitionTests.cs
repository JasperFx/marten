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

        [Fact]
        public void equivalency_with_the_postgres_synonym_issue()
        {
            // This was meant to address GH-127

            var users = DocumentMapping.For<User>();
            users.DuplicateField("FirstName");

            var table1 = users.ToTable(null);
            var table2 = users.ToTable(null);

            table1.Column("first_name").Type = "varchar";
            table2.Column("first_name").Type = "character varying";

            table1.Equals(table2).ShouldBeTrue();
            table2.Equals(table1).ShouldBeTrue();

            table1.Column("first_name").Type = "character varying";
            table2.Column("first_name").Type = "varchar";

            table1.Equals(table2).ShouldBeTrue();
            table2.Equals(table1).ShouldBeTrue();

            table1.Column("first_name").Type = "character varying";
            table2.Column("first_name").Type = "character varying";

            table1.Equals(table2).ShouldBeTrue();
            table2.Equals(table1).ShouldBeTrue();

            table1.Column("first_name").Type = "varchar";
            table2.Column("first_name").Type = "varchar";

            table1.Equals(table2).ShouldBeTrue();
            table2.Equals(table1).ShouldBeTrue();
        }
    }
}