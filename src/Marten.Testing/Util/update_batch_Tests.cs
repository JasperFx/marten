using System;
using System.Linq;
using System.Threading.Tasks;
using Baseline;
using Marten.Schema;
using Marten.Services;
using Marten.Services.Deletes;
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
                await session.SaveChangesAsync().ConfigureAwait(false);

                var t = await session.Query<Target>().CountAsync().ConfigureAwait(false);
                t.ShouldBe(100);
            }
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

            var upsert = theMapping.UpsertFunction;

            var serializer = new JilSerializer();

            batch.Sproc(upsert).Param("docId", target1.Id).JsonBody("doc", serializer.ToJson(target1)).Param("docVersion", Guid.NewGuid()).Param("docDotNetType", typeof(Target).AssemblyQualifiedName);
            batch.Sproc(upsert).Param("docId", target2.Id).JsonBody("doc", serializer.ToJson(target2)).Param("docVersion", Guid.NewGuid()).Param("docDotNetType", typeof(Target).AssemblyQualifiedName);
            batch.Sproc(upsert).Param("docId", target3.Id).JsonBody("doc", serializer.ToJson(target3)).Param("docVersion", Guid.NewGuid()).Param("docDotNetType", typeof(Target).AssemblyQualifiedName);



            batch.Execute();
            batch.Connection.Dispose();

            var targets = theSession.Query<Target>().ToArray();

            targets.Any(x => x.Id == target1.Id).ShouldBeTrue();
            targets.Any(x => x.Id == target2.Id).ShouldBeTrue();
            targets.Any(x => x.Id == target3.Id).ShouldBeTrue();

        }
    }
}