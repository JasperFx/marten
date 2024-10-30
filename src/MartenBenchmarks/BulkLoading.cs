using System.Linq;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Marten.Testing;
using Marten.Testing.Documents;

namespace MartenBenchmarks;

[SimpleJob(warmupCount: 2)]
[MemoryDiagnoser]
public class BulkLoading
{
    public static Target[] Docs = Target.GenerateRandomData(1000).ToArray();

    [GlobalSetup]
    public async Task Setup()
    {
        await BenchmarkStore.Store.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(Target));
    }

    [Benchmark]
    public async Task BulkInsertDocuments()
    {
        await BenchmarkStore.Store.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(Target));
        await BenchmarkStore.Store.BulkInsertAsync(Docs);
    }
}
