using System;
using System.Linq;
using System.Threading.Tasks;
using Baseline;
using Marten.Schema;
using Marten.Services;
using Marten.Services.Deletes;
using Marten.Util;
using NpgsqlTypes;
using Shouldly;
using Xunit;

namespace Marten.Testing.Util
{
    public class update_batch_Tests : DocumentSessionFixture<NulloIdentityMap>
    {
        private readonly DocumentMapping theMapping;

        public update_batch_Tests()
        {
            theMapping = theStore.Tenancy.Default.MappingFor(typeof (Target)).As<DocumentMapping>();
        }

        [Fact]
        public void can_make_updates_with_more_than_one_batch()
        {
            var targets = Target.GenerateRandomData(100).ToArray();
            StoreOptions(_ => _.UpdateBatchSize = 10);

            using (var session = theStore.LightweightSession())
            {
                targets.Each(x => session.Store(x));
                session.SaveChanges();

                session.Query<Target>().Count().ShouldBe(100);
            }
        }


        [Fact]
        public async Task can_make_updates_with_more_than_one_batch_async()
        {
            StoreOptions(_ => { _.UpdateBatchSize = 10; });

            var targets = Target.GenerateRandomData(100).ToArray();

            using (var session = theStore.LightweightSession())
            {
                session.Store(targets);
                await session.SaveChangesAsync().ConfigureAwait(false);

                var t = await session.Query<Target>().CountAsync().ConfigureAwait(false);
                t.ShouldBe(100);
            }
        }

        [Fact]
        public void can_delete_and_make_updates_with_more_than_one_batch_GH_987()
        {
            var targets = Target.GenerateRandomData(100).ToArray();
            StoreOptions(_ => _.UpdateBatchSize = 10);

            using (var session = theStore.LightweightSession())
            {
                session.DeleteWhere<Target>(t => t.Id != null);

                
                targets.Each(x => session.Store(x));
                session.SaveChanges();

                session.Query<Target>().Count().ShouldBe(100);
            }
        }
    }
}