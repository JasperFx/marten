using Marten.Services;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug_237_duplicate_indexing_Tests: BugIntegrationContext
    {
        [Fact]
        public void save()
        {
            StoreOptions(_ =>
            {
                _.Schema.For<Issue>()
                .Duplicate(x => x.AssigneeId)
                .ForeignKey<User>(x => x.AssigneeId);
            });

            theSession.Store(new Issue());
            theSession.SaveChanges();
        }

    }
}
