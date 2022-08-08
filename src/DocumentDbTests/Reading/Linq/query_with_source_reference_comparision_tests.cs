using System;
using System.Linq;
using Marten.Testing.Harness;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Documents;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Reading.Linq
{
    public class query_with_source_reference_comparision_tests: IntegrationContext
    {
        public query_with_source_reference_comparision_tests(DefaultStoreFixture fixture) : base(fixture)
        {
        }

        [Fact]
        public async Task ignore_source_reference_comparision()
        {
            for (var i = 0; i < 10; i++)
            {
                var issueTitle = i % 2 == 0 ? "E" : "O";
                var issue = new Issue {Id = Guid.NewGuid(), Title = issueTitle};
                theSession.Store(issue);
            }
            await theSession.SaveChangesAsync();
            var issues = await theSession.Query<Issue>()
                .Where(i => i != null)
                .ToListAsync();
            issues.Count.ShouldBe(10);
        }

        [Fact]
        public async Task ignore_nested_source_reference_comparision()
        {
            for (var i = 0; i < 10; i++)
            {
                var issueTitle = i % 2 == 0 ? "E" : "O";
                var issue = new Issue {Id = Guid.NewGuid(), Title = issueTitle};
                theSession.Store(issue);
            }
            await theSession.SaveChangesAsync();
            var issues = await theSession.Query<Issue>()
                .Where(i => (i != null && i.Title == "E") || (i == null && i.Title == "E"))
                .ToListAsync();
            issues.Count.ShouldBe(5);
        }
    }
}
