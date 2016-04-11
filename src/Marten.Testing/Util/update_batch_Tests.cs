using System.Linq;
using System.Threading.Tasks;
using Baseline;
using Marten.Schema;
using Marten.Services;
using Marten.Testing.Fixtures;
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
            theMapping = theStore.Schema.MappingFor(typeof (Target)).As<DocumentMapping>();
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
                await session.SaveChangesAsync();

                var t = await session.Query<Target>().CountAsync();
                t.ShouldBe(100);
            }
        }

        [Fact]
        public void write_multiple_calls()
        {
            // Just forcing the table and schema objects to be created
            var initialTarget = Target.Random();
            theSession.Store(initialTarget);
            theSession.SaveChanges();

            var batch = theStore.Advanced.CreateUpdateBatch();

            var target1 = Target.Random();
            var target2 = Target.Random();
            var target3 = Target.Random();

            var upsertName = theMapping.QualifiedUpsertName;


            batch.Sproc(upsertName).Param("docId", target1.Id).JsonEntity("doc", target1);
            batch.Sproc(upsertName).Param("docId", target2.Id).JsonEntity("doc", target2);
            batch.Sproc(upsertName).Param("docId", target3.Id).JsonEntity("doc", target3);
            batch.Delete(theMapping.QualifiedTableName, initialTarget.Id, NpgsqlDbType.Uuid);

            batch.Execute();
            batch.Connection.Dispose();

            var targets = theSession.Query<Target>().ToArray();
            targets.Count().ShouldBe(3);

            targets.Any(x => x.Id == target1.Id).ShouldBeTrue();
            targets.Any(x => x.Id == target2.Id).ShouldBeTrue();
            targets.Any(x => x.Id == target3.Id).ShouldBeTrue();

            targets.Any(x => x.Id == initialTarget.Id).ShouldBeFalse();
        }


        [Fact]
        public void write_multiple_calls_with_json_supplied()
        {
            // Just forcing the table and schema objects to be created
            var initialTarget = Target.Random();
            theSession.Store(initialTarget);
            theSession.SaveChanges();

            var batch = theStore.Advanced.CreateUpdateBatch();

            var target1 = Target.Random();
            var target2 = Target.Random();
            var target3 = Target.Random();

            var upsertName = theMapping.QualifiedUpsertName;

            var serializer = new JsonNetSerializer();

            batch.Sproc(upsertName).Param("docId", target1.Id).JsonBody("doc", serializer.ToJson(target1));
            batch.Sproc(upsertName).Param("docId", target2.Id).JsonBody("doc", serializer.ToJson(target2));
            batch.Sproc(upsertName).Param("docId", target3.Id).JsonBody("doc", serializer.ToJson(target3));
            batch.Delete(theMapping.QualifiedTableName, initialTarget.Id, NpgsqlDbType.Uuid);

            batch.Execute();
            batch.Connection.Dispose();

            var targets = theSession.Query<Target>().ToArray();
            targets.Count().ShouldBe(3);

            targets.Any(x => x.Id == target1.Id).ShouldBeTrue();
            targets.Any(x => x.Id == target2.Id).ShouldBeTrue();
            targets.Any(x => x.Id == target3.Id).ShouldBeTrue();

            targets.Any(x => x.Id == initialTarget.Id).ShouldBeFalse();
        }
    }
}