using System.Linq;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Attributes.Exporters;
using BenchmarkDotNet.Attributes.Jobs;
using Marten.Testing;

namespace MartenBenchmarks
{
    [SimpleJob(warmupCount: 2)]
    public class DocumentActions
    {
        public static Target[] Docs = Target.GenerateRandomData(100).ToArray();


        [Setup]
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