using System.Linq;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Marten.Testing;
using Marten.Testing.Documents;

namespace MartenBenchmarks;

[SimpleJob(warmupCount: 2)]
[MemoryDiagnoser]
public class DocumentActions
{
    public static Target[] Docs = Target.GenerateRandomData(100).ToArray();

    [GlobalSetup]
    public async Task Setup()
    {
        await BenchmarkStore.Store.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(Target));
    }

    [Benchmark]
    public async Task InsertDocuments()
    {
        using var session = BenchmarkStore.Store.LightweightSession();
        session.Store(Docs);
        await session.SaveChangesAsync();
    }
}
