using Marten.Services;
using Marten.Testing.Documents;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug_237_duplicate_indexing_Tests : DocumentSessionFixture<NulloIdentityMap>
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