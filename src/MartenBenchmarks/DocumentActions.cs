using System.Linq;
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
    public void Setup()
    {
        BenchmarkStore.Store.Advanced.Clean.DeleteDocumentsByType(typeof(Target));
    }

    [Benchmark]
    public void InsertDocuments()
    {
        using var session = BenchmarkStore.Store.LightweightSession();
        session.Store(Docs);
        session.SaveChanges();
    }
}
