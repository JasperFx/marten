using System.Linq;
using BenchmarkDotNet.Attributes;
using Marten.Testing;

namespace MartenBenchmarks
{
    [SimpleJob(warmupCount: 2)]
    public class DocumentActions
    {
        public static Target[] Docs = Target.GenerateRandomData(100).ToArray();


        [GlobalSetup]
        public void Setup()
        {
            BenchmarkStore.Store.Advanced.Clean.DeleteDocumentsFor(typeof(Target));
        }

        [Benchmark]
        [MemoryDiagnoser]
        
        public void InsertDocuments()
        {
            using (var session = BenchmarkStore.Store.OpenSession())
            {
                session.Store(Docs);
                session.SaveChanges();
            }
        }


    }
}