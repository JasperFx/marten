using System.Linq;
using System.Threading.Tasks;
using Marten.Exceptions;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace DocumentDbTests.Bugs
{
    public class Bug_1994_insert_update_on_same_object_in_transaction: BugIntegrationContext
    {
        private readonly ITestOutputHelper _output;

        public Bug_1994_insert_update_on_same_object_in_transaction(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task bug_1994_pending_changes_after_insert_and_store_on_same_object()
        {
            StoreOptions(x => x.Schema.For<User>().UseOptimisticConcurrency(true));

            var user1 = new User();

            await using var session1 = theStore.LightweightSession();
            session1.Logger = new TestOutputMartenLogger(_output);

            session1.Insert(user1);


            session1.PendingChanges.InsertsFor<User>()
                .Single().ShouldBeTheSameAs(user1);

            session1.PendingChanges.AllChangedFor<User>()
                .Contains(user1).ShouldBeTrue();

            session1.Update(user1);

            await Should.ThrowAsync<ConcurrencyException>(async () =>
            {
                await session1.SaveChangesAsync();
            });


        }
    }
}
