using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Marten.Schema;
using Marten.Services;
using Marten.Testing.Fixtures;
using Marten.Util;
using NpgsqlTypes;
using Shouldly;
using StructureMap;

namespace Marten.Testing.Util
{
    public class update_batch_Tests : IntegratedFixture
    {
        private DocumentMapping theMapping;
        private IDocumentSession theSession;

        public update_batch_Tests()
        {
            theMapping = theContainer.GetInstance<IDocumentSchema>().MappingFor(typeof (Target));
            theSession = theContainer.GetInstance<IDocumentStore>().OpenSession();
        }

        public void write_multiple_calls()
        {
            // Just forcing the table and schema objects to be created
            var initialTarget = Target.Random();
            theSession.Store(initialTarget);
            theSession.SaveChanges();

            var batch = theContainer.GetInstance<UpdateBatch>();

            var target1 = Target.Random();
            var target2 = Target.Random();
            var target3 = Target.Random();

            var upsertName = theMapping.UpsertName;

            batch.Sproc(upsertName).Param(target1.Id).JsonEntity(target1);
            batch.Sproc(upsertName).Param(target2.Id).JsonEntity(target2);
            batch.Sproc(upsertName).Param(target3.Id).JsonEntity(target3);
            batch.Delete(theMapping.TableName, initialTarget.Id, NpgsqlDbType.Uuid);

            batch.Execute();

            var targets = theSession.Query<Target>().ToArray();
            targets.Count().ShouldBe(3);

            targets.Any(x => x.Id == target1.Id).ShouldBeTrue();
            targets.Any(x => x.Id == target2.Id).ShouldBeTrue();
            targets.Any(x => x.Id == target3.Id).ShouldBeTrue();

            targets.Any(x => x.Id == initialTarget.Id).ShouldBeFalse();
        }


        public void write_multiple_calls_with_json_supplied()
        {
            // Just forcing the table and schema objects to be created
            var initialTarget = Target.Random();
            theSession.Store(initialTarget);
            theSession.SaveChanges();

            var batch = theContainer.GetInstance<UpdateBatch>();

            var target1 = Target.Random();
            var target2 = Target.Random();
            var target3 = Target.Random();

            var upsertName = theMapping.UpsertName;

            var serializer = theContainer.GetInstance<ISerializer>();

            batch.Sproc(upsertName).Param(target1.Id).JsonBody(serializer.ToJson(target1));
            batch.Sproc(upsertName).Param(target2.Id).JsonBody(serializer.ToJson(target2));
            batch.Sproc(upsertName).Param(target3.Id).JsonBody(serializer.ToJson(target3));
            batch.Delete(theMapping.TableName, initialTarget.Id, NpgsqlDbType.Uuid);

            batch.Execute();

            var targets = theSession.Query<Target>().ToArray();
            targets.Count().ShouldBe(3);

            targets.Any(x => x.Id == target1.Id).ShouldBeTrue();
            targets.Any(x => x.Id == target2.Id).ShouldBeTrue();
            targets.Any(x => x.Id == target3.Id).ShouldBeTrue();

            targets.Any(x => x.Id == initialTarget.Id).ShouldBeFalse();
        }



        
    }
}