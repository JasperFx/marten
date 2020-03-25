using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Baseline;
using Marten;
using Marten.Testing;
using Shouldly;
using Xunit;

namespace Marten.Testing.Schema.Identity.Sequences
{
    public class Bug_1404_Hilo_concurrent_update_failure
    {
        [Fact]
        public void generate_hilo_in_highly_concurrent_scenarios()
        {
            // ensure we create required DB objects since the concurrent
            // test could potentially create the same DB objects at the same time
            var store = CreateDocumentStore();
            using(var session = store.OpenSession())
            {
                session.InsertObjects(TargetIntId.GenerateRandomData(1));
                session.SaveChanges();
            }

            Task.WaitAll(new Task[]
            {
                Task.Run(()=> Hammertime()),
                Task.Run(()=> Hammertime()),
                Task.Run(()=> Hammertime()),
                Task.Run(()=> Hammertime())
            });
        }

        private void Hammertime()
        {
            using (var store = CreateDocumentStore())
            {
                using (var session = store.OpenSession())
                {
                    var targets = TargetIntId.GenerateRandomData(100);
                    session.InsertObjects(targets);
                    session.SaveChanges();
                }
                
            }
        }

        private DocumentStore CreateDocumentStore()
        {
            return DocumentStore.For(_ =>
            {
                _.HiloSequenceDefaults.MaxLo = 5;
                _.Connection(ConnectionSource.ConnectionString);
                _.AutoCreateSchemaObjects = AutoCreate.All;
            });
        }
    }
}