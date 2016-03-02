using System.Linq;
using Marten.Generation;
using Marten.Schema;
using Marten.Testing.Documents;
using Shouldly;
using Xunit;

namespace Marten.Testing.Generation
{
    public class TableDiffTests
    {
        [Fact]
        public void perfect_match()
        {
            var users = new DocumentMapping(typeof(User));
            var actual = users.ToTable(null);
            var expected = users.ToTable(null);

            var diff = new TableDiff(expected, actual);
            diff.Matches.ShouldBeTrue();
        }

        [Fact]
        public void can_match_up_on_columns()
        {
            var users = new DocumentMapping(typeof(User));
            var actual = users.ToTable(null);
            var expected = users.ToTable(null);

            var diff = new TableDiff(expected, actual);

            diff.Matched.OrderBy(x => x.Name).Select(x => x.Name)
                .ShouldHaveTheSameElementsAs("data", "id");
        }

        [Fact]
        public void not_matching_with_missing_columns()
        {
            var users = new DocumentMapping(typeof(User));
            var actual = users.ToTable(null);

            var expected = users.ToTable(null);
            var tableColumn = new TableColumn("new", "varchar");
            expected.Columns.Add(tableColumn);


            var diff = new TableDiff(expected, actual);
            diff.Matches.ShouldBeFalse();

            diff.Missing.Single().ShouldBe(tableColumn);
            diff.Extras.Any().ShouldBeFalse();
            diff.Different.Any().ShouldBeFalse();
        }

        [Fact]
        public void not_matching_with_extra_columns()
        {
            var users = new DocumentMapping(typeof(User));
            var actual = users.ToTable(null);
            var tableColumn = new TableColumn("new", "varchar");
            actual.Columns.Add(tableColumn);

            var expected = users.ToTable(null);

            var diff = new TableDiff(expected, actual);

            diff.Matches.ShouldBeFalse();
            diff.Extras.Single().ShouldBe(tableColumn);
        }

        [Fact]
        public void not_matching_with_columns_of_same_name_that_are_different()
        {
            var users = new DocumentMapping(typeof(User));
            var actual = users.ToTable(null);
            actual.Column("id").Type = "int";

            var expected = users.ToTable(null);

            var diff = new TableDiff(expected, actual);
            diff.Matches.ShouldBeFalse();

            diff.Different.Single().Name.ShouldBe("id");
        }
    }
}