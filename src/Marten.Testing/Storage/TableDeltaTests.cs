using System.Linq;
using Marten.Schema;
using Marten.Storage;
using Marten.Testing.Documents;
using Shouldly;
using Xunit;

namespace Marten.Testing.Storage
{
    public class TableDeltaTests
    {
        [Fact]
        public void perfect_match()
        {
            var users = DocumentMapping.For<User>();
            var actual = new DocumentTable(users);
            var expected = new DocumentTable(users);

            var diff = new TableDelta(expected, actual);
            diff.Matches.ShouldBeTrue();
        }

        [Fact]
        public void can_match_up_on_columns()
        {
            var users = DocumentMapping.For<User>();
            var actual = new DocumentTable(users);
            var expected = new DocumentTable(users);

            var diff = new TableDelta(expected, actual);

            diff.Matched.OrderBy(x => x.Name).Select(x => x.Name)
                .ShouldHaveTheSameElementsAs("data", "id", DocumentMapping.DotNetTypeColumn, DocumentMapping.LastModifiedColumn, DocumentMapping.VersionColumn);
        }

        [Fact]
        public void not_matching_with_missing_columns()
        {
            var users = DocumentMapping.For<User>();
            var actual = new DocumentTable(users);
            var expected = new DocumentTable(users);

            var tableColumn = new TableColumn("new", "varchar");
            expected.AddColumn(tableColumn);


            var diff = new TableDelta(expected, actual);
            diff.Matches.ShouldBeFalse();

            diff.Missing.Single().ShouldBe(tableColumn);
            diff.Extras.Any().ShouldBeFalse();
            diff.Different.Any().ShouldBeFalse();
        }

        [Fact]
        public void not_matching_with_extra_columns()
        {
            var users = DocumentMapping.For<User>();
            var actual = new DocumentTable(users);
            var expected = new DocumentTable(users);

            var tableColumn = new TableColumn("new", "varchar");
            actual.AddColumn(tableColumn);

            var diff = new TableDelta(expected, actual);

            diff.Matches.ShouldBeFalse();
            diff.Extras.Single().ShouldBe(tableColumn);
        }

        [Fact]
        public void not_matching_with_columns_of_same_name_that_are_different()
        {
            var users = DocumentMapping.For<User>();
            var actual = new DocumentTable(users);
            var expected = new DocumentTable(users);

            actual.ReplaceOrAddColumn("id", "int");

            var diff = new TableDelta(expected, actual);
            diff.Matches.ShouldBeFalse();

            diff.Different.Single().Name.ShouldBe("id");
        }
    }
}