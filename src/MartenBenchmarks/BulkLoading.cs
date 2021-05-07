using System.Linq;
using BenchmarkDotNet.Attributes;
using Marten.Testing;
using Marten.Testing.Documents;

namespace MartenBenchmarks
{
    [SimpleJob(warmupCount: 2)]
    [MemoryDiagnoser]
    public class BulkLoading
    {
        public static Target[] Docs = Target.GenerateRandomData(1000).ToArray();

        [GlobalSetup]
        public void Setup()
        {
            BenchmarkStore.Store.Advanced.Clean.DeleteDocumentsByType(typeof(Target));
        }

        [Benchmark]
        public void BulkInsertDocuments()
        {
            BenchmarkStore.Store.Advanced.Clean.DeleteDocumentsByType(typeof(Target));
            BenchmarkStore.Store.BulkInsert(Docs);
        }
    }
}
