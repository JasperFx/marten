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
        public async void only_generated_once_default_connection_string_schema()
        {
            await Task.WhenAll(new Task[]
            {
                Task.Run(()=> Hammertime()),
                Task.Run(()=> Hammertime()),
                Task.Run(()=> Hammertime()),
                Task.Run(()=> Hammertime())
            });
        }

        private void Hammertime()
        {
            using (var store = DocumentStore.For(_ =>
            {
                 _.HiloSequenceDefaults.MaxLo = 1;
                 _.Connection(ConnectionSource.ConnectionString);
                 _.AutoCreateSchemaObjects = AutoCreate.All;
             }))
            {
                using (var session = store.OpenSession())
                {
                    var targets = TargetIntId.GenerateRandomData(100);
                    session.InsertObjects(targets);
                    session.SaveChanges();
                }
                
            }
        }
    }
}