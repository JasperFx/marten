using System.Threading.Tasks;
using Marten.Schema.Testing.Documents;
using Marten.Testing.Harness;
using Xunit;

namespace Marten.Schema.Testing.Identity.Sequences
{
    public class Bug_1404_Hilo_concurrent_update_failure
    {
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
                _.Advanced.HiloSequenceDefaults.MaxLo = 5;
                _.Connection(ConnectionSource.ConnectionString);
                _.AutoCreateSchemaObjects = AutoCreate.All;
            });
        }

        [Fact]
        public void generate_hilo_in_highly_concurrent_scenarios()
        {
            // ensure we create required DB objects since the concurrent
            // test could potentially create the same DB objects at the same time
            var store = CreateDocumentStore();
            using (var session = store.OpenSession())
            {
                session.InsertObjects(TargetIntId.GenerateRandomData(1));
                session.SaveChanges();
            }

            Task.WaitAll(Task.Run(() => Hammertime()), Task.Run(() => Hammertime()), Task.Run(() => Hammertime()),
                Task.Run(() => Hammertime()));
        }
    }
}
