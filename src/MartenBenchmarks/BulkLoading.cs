using System.Linq;
using BenchmarkDotNet.Attributes;
using Marten.Testing;

namespace MartenBenchmarks
{
    [SimpleJob(warmupCount: 2)]

    public class BulkLoading
    {
        public static Target[] Docs = Target.GenerateRandomData(1000).ToArray();

        [GlobalSetup]
        public void Setup()
        {
            BenchmarkStore.Store.Advanced.Clean.DeleteDocumentsFor(typeof(Target));
        }

        [Benchmark]
        [MemoryDiagnoser]
        public void BulkInsertDocuments()
        {
            BenchmarkStore.Store.Advanced.Clean.DeleteDocumentsFor(typeof(Target));
            BenchmarkStore.Store.BulkInsert(Docs);
        }
    }
}