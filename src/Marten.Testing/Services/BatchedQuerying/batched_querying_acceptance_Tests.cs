using System.Threading.Tasks;
using Marten.Services;
using Marten.Testing.Fixtures;
using Shouldly;
using Xunit;

namespace Marten.Testing.Services.BatchedQuerying
{
    public class batched_querying_acceptance_Tests : DocumentSessionFixture<IdentityMap>
    {
        private readonly Target target1 = Target.Random();
        private readonly Target target2 = Target.Random();
        private readonly Target target3 = Target.Random();

        public batched_querying_acceptance_Tests()
        {
            theSession.Store(target1, target2, target3);
            theSession.SaveChanges();
        }

        [Fact]
        public async Task can_find_one_doc_at_a_time_that_is_not_in_identity_map()
        {
            var batch = theSession.CreateBatchQuery();
            var task1 = batch.Load<Target>(target1.Id);
            var task3 = batch.Load<Target>(target3.Id);

            await batch.Execute();

            (await task1).ShouldBeOfType<Target>().ShouldNotBeNull();
            (await task3).ShouldBeOfType<Target>().ShouldNotBeNull();
        }

        [Fact]
        public async Task can_find_docs_by_id_that_should_be_in_identity_map()
        {
            var batch1 = theSession.CreateBatchQuery();
            var task1 = batch1.Load<Target>(target1.Id);
            var task3 = batch1.Load<Target>(target3.Id);

            await batch1.Execute();

            var batch2 = theSession.CreateBatchQuery();
            var task21 = batch2.Load<Target>(target1.Id);
            var task23 = batch2.Load<Target>(target3.Id);

            await batch2.Execute();

            (await task1).ShouldBeSameAs(await task21);
            (await task3).ShouldBeSameAs(await task23);
        }
    }
}