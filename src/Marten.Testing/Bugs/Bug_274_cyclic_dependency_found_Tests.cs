using Marten.Services;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug_274_cyclic_dependency_found_Tests: BugIntegrationContext
    {
        [Fact]
        public void save()
        {
            StoreOptions(_ =>
            {
                _.Schema.For<Issue>()
                    .ForeignKey<User>(x => x.AssigneeId)
                    .ForeignKey<User>(x => x.ReporterId);
            });

            theSession.Store(new Issue());
            theSession.SaveChanges();
        }

    }
}
