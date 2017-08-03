using System.Linq;
using System.Threading.Tasks;
using Shouldly;
using StructureMap.Building;
using Xunit;

namespace Marten.Testing.MultiTenancy
{
    public class with_batched_queries : IntegratedFixture
    {
        private Target[] _reds = Target.GenerateRandomData(100).ToArray();
        private Target[] _greens = Target.GenerateRandomData(100).ToArray();

        public with_batched_queries()
        {
            StoreOptions(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.Policies.AllDocumentsAreMultiTenanted();
            });

            theStore.BulkInsert("Red", _reds);
            theStore.BulkInsert("Green", _greens);
        }

        [Fact]
        public async Task query_with_batch()
        {
            using (var query = theStore.QuerySession("Red"))
            {
                var batch = query.CreateBatchQuery();

                var foundRed = batch.Load<Target>(_reds[0].Id);
                var notFoundGreen = batch.Load<Target>(_greens[0].Id);

                var queryForReds = batch.Query<Target>().Where(x => x.Flag).ToList();

                var groupOfReds = batch.LoadMany<Target>().ById(_reds[0].Id, _reds[1].Id, _greens[0].Id, _greens[1].Id);

                await batch.Execute();

                (await foundRed).ShouldNotBeNull();
                (await notFoundGreen).ShouldBeNull();

                var found = await queryForReds;

                found.Any(x => _greens.Any(t => t.Id == x.Id)).ShouldBeFalse();

                var reds = await groupOfReds;

                reds.Count.ShouldBe(2);
                reds.Any(x => x.Id == _reds[0].Id).ShouldBeTrue();
                reds.Any(x => x.Id == _reds[1].Id).ShouldBeTrue();

            }
        }
    }
}