using Baseline;
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
            var users = DocumentSchemaObjects.For<User>();
            var table1 = users.ToTable(null);
            var table2 = users.ToTable(null);

            table2.ShouldBe(table1);
            table1.ShouldBe(table2);
            table1.ShouldNotBeSameAs(table2);
        }

        [Fact]
        public void equivalency_negative_different_numbers_of_columns()
        {
            var users = DocumentSchemaObjects.For<User>();
            var table1 = users.ToTable(null);
            var table2 = users.ToTable(null);

            table2.Columns.Add(new TableColumn("user_name", "character varying"));

            table2.ShouldNotBe(table1);
        }

        [Fact]
        public void equivalency_negative_column_type_changed()
        {
            var users = DocumentSchemaObjects.For<User>();
            var table1 = users.ToTable(null);
            var table2 = users.ToTable(null);

            table2.ReplaceOrAddColumn(table2.PrimaryKey.Name, "int", table2.PrimaryKey.Directive);

            table2.ShouldNotBe(table1);
        }

        [Fact]
        public void equivalency_positive_column_name_case_insensitive()
        {
            var users = DocumentSchemaObjects.For<User>();
            var table1 = users.ToTable(null);
            var table2 = users.ToTable(null);

            table2.Column("username").ShouldBeSameAs(table1.Column("UserName"));

            table2.ShouldBe(table1);
        }

        [Fact]
        public void equivalency_with_the_postgres_synonym_issue()
        {
            // This was meant to address GH-127

            var users = DocumentMapping.For<User>();
            users.DuplicateField("FirstName");

            var table1 = users.SchemaObjects.As<DocumentSchemaObjects>().ToTable(null);
            var table2 = users.SchemaObjects.As<DocumentSchemaObjects>().ToTable(null);

            table1.ReplaceOrAddColumn("first_name", "varchar");
            table2.ReplaceOrAddColumn("first_name", "character varying");

            table1.Equals(table2).ShouldBeTrue();
            table2.Equals(table1).ShouldBeTrue();

            table1.ReplaceOrAddColumn("first_name", "character varying");
            table2.ReplaceOrAddColumn("first_name", "varchar");

            table1.Equals(table2).ShouldBeTrue();
            table2.Equals(table1).ShouldBeTrue();

            table1.ReplaceOrAddColumn("first_name", "character varying");
            table2.ReplaceOrAddColumn("first_name", "character varying");

            table1.Equals(table2).ShouldBeTrue();
            table2.Equals(table1).ShouldBeTrue();

            table1.ReplaceOrAddColumn("first_name", "varchar");
            table2.ReplaceOrAddColumn("first_name", "varchar");

            table1.Equals(table2).ShouldBeTrue();
            table2.Equals(table1).ShouldBeTrue();
        }
    }
}