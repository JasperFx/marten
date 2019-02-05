using System.Linq;
using BenchmarkDotNet.Attributes;
using Marten;
using Marten.Services;
using MartenBenchmarks.BenchAgainst;
using MartenBenchmarks.Infrastructure;

namespace MartenBenchmarks.Benchmarks
{
    [MemoryDiagnoser]
    public class IdentityMapBench
    {
        [Params(10, 100, 1000)] public int ItemCount;

        private readonly IdentityMap identityMapWithImHashMap = new IdentityMap(new JsonNetSerializer(), null);
        private readonly IdentityMapBaseline identityMapWithConcurrentDictionary = new IdentityMapBaseline(new JsonNetSerializer(), null);
        private readonly ISerializer serializer = new JsonNetSerializer();

        private BenchModel[] values;

        [GlobalSetup]
        public void GlobalSetup()
        {
            values = Enumerable.Range(0, ItemCount).Select(x => new BenchModel()).ToArray();
        }

        [Benchmark(Baseline = true)]
        public void PopulateAndQueryIdentityMapWithConcurrentDictionary()
        {
            foreach (var v in values)
            {
                identityMapWithConcurrentDictionary.Get<BenchModel>(v.Id, serializer.ToJson(v).ToReader(), null);
            }

            foreach (var v in values)
            {
                identityMapWithConcurrentDictionary.Get<BenchModel>(v.Id, serializer.ToJson(v).ToReader(), null);
            }
        }

        [Benchmark]
        public void PopulateAndQueryIdentityMapWithImHashMap()
        {
            foreach (var v in values)
            {
                identityMapWithImHashMap.Get<BenchModel>(v.Id, serializer.ToJson(v).ToReader(), null);
            }

            foreach (var v in values)
            {
                identityMapWithConcurrentDictionary.Get<BenchModel>(v.Id, serializer.ToJson(v).ToReader(), null);
            }
        }
    }
}