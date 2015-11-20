using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Marten.Schema;
using Marten.Services;
using Marten.Testing.Fixtures;
using Marten.Util;
using NpgsqlTypes;
using Shouldly;

namespace Marten.Testing.Util
{
    public class update_batch_Tests : DocumentSessionFixture
    {
        private DocumentMapping theMapping;

        public update_batch_Tests()
        {
            theMapping = theContainer.GetInstance<IDocumentSchema>().MappingFor(typeof (Target));
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

            batch.Sproc(upsertName).Param(target1.Id).JsonParam(target1);
            batch.Sproc(upsertName).Param(target2.Id).JsonParam(target2);
            batch.Sproc(upsertName).Param(target3.Id).JsonParam(target3);
            batch.Delete(theMapping.TableName, initialTarget.Id, NpgsqlDbType.Uuid);

            batch.Execute();

            var targets = theSession.Query<Target>().ToArray();
            targets.Count().ShouldBe(3);

            targets.Any(x => x.Id == target1.Id).ShouldBeTrue();
            targets.Any(x => x.Id == target2.Id).ShouldBeTrue();
            targets.Any(x => x.Id == target3.Id).ShouldBeTrue();

            targets.Any(x => x.Id == initialTarget.Id).ShouldBeFalse();
        }

        /*
        public void time_perf()
        {
            var targets = Target.GenerateRandomData(50);


            var time = Timings.Time(() =>
            {
                targets.Each(x => theSession.Store(x));
                theSession.SaveChanges();
            });

            Debug.WriteLine("SaveChanges() is " + time);

            theContainer.GetInstance<IDocumentCleaner>().DeleteAllDocuments();

            var batch = theContainer.GetInstance<UpdateBatch>();
            

            var batchTime = Timings.Time(() =>
            {
                targets.Each(x => batch.Sproc(theMapping.UpsertName).Param(x.Id).JsonParam(x));
                batch.Execute();
            });

            Debug.WriteLine("UpdateBatch time: " + batchTime);

            theContainer.GetInstance<IDocumentCleaner>().DeleteAllDocuments();
            var bulkTime = Timings.Time(() => theSession.BulkInsert(targets.ToArray()));

            Debug.WriteLine("The bulk insert time was " + bulkTime);
        }
        */
    }
}