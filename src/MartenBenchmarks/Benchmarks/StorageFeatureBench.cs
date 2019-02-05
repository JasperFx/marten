using System;
using BenchmarkDotNet.Attributes;
using Marten;
using Marten.Storage;
using MartenBenchmarks.BenchAgainst;

namespace MartenBenchmarks.Benchmarks
{
    [MemoryDiagnoser]
    public class StorageFeatureBench
    {
        private readonly StorageFeatures storageFeaturesWithImHashMap = new StorageFeatures(new StoreOptions());
        private readonly StorageFeaturesWithConcurrentDictionary storageFeaturesWithConcurrentDictionary = new StorageFeaturesWithConcurrentDictionary(new StoreOptions());

        private Type[] values;

        [GlobalSetup]
        public void GlobalSetup()
        {
            values = new[] {typeof(BenchModel), typeof(BenchModel2), typeof(BenchModel3), typeof(BenchModel4) };
        }

        [Benchmark(Baseline = true)]
        public void PopulateAndQueryStorageFeaturesWithConcurrentDictionary()
        {
            foreach (var v in values)
            {
                storageFeaturesWithConcurrentDictionary.MappingFor(v);
            }

            foreach (var v in values)
            {
                storageFeaturesWithConcurrentDictionary.MappingFor(v);
            }
        }

        [Benchmark]
        public void PopulateAndQueryStorageFeaturesWithImHashMap()
        {
            foreach (var v in values)
            {
                storageFeaturesWithImHashMap.MappingFor(v);
            }

            foreach (var v in values)
            {
                storageFeaturesWithImHashMap.MappingFor(v);
            }
        }
    }
}