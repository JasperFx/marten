using System.Linq;
using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using Marten;
using Marten.Services;
using MartenBenchmarks.BenchAgainst;

namespace MartenBenchmarks.Benchmarks
{
    [MemoryDiagnoser]
    public class UnitOfWorkBench
    {
        [Params(500)] public int ItemCount;

        private UnitOfWorkBaseline unitOfWorkWithConcurrentDictionary;
        private UnitOfWork unitOfWorkWithImHashMap;

        private BenchModel[] values;

        [GlobalSetup]
        public void GlobalSetup()
        {
            // Won't matter - we don't initiate connections
            var cstring = "Server=127.0.0.1;Port=5432;Database=myDataBase;User Id=myUsername;Password=myPassword;";

            var store = DocumentStore.For(s =>
            {
                s.AutoCreateSchemaObjects = AutoCreate.None;
                s.Connection(cstring);
            });

            unitOfWorkWithConcurrentDictionary = new UnitOfWorkBaseline(store, store.Tenancy.Default);

            var store2 = DocumentStore.For(s =>
            {
                s.AutoCreateSchemaObjects = AutoCreate.None;
                s.Connection(cstring);
            });

            unitOfWorkWithImHashMap = new UnitOfWork(store2, store2.Tenancy.Default);

            values = Enumerable.Range(0, ItemCount).Select(x => new BenchModel()).ToArray();
        }


        [MethodImpl(MethodImplOptions.NoInlining)]
        private void Nop(object obj)
        {

        }

        [Benchmark(Baseline = true)]
        public void PopulateAndQueryUnitOfWorkWithConcurrentDictionary()
        {
            foreach (var v in values)
            {
                unitOfWorkWithConcurrentDictionary.StoreInserts(v);
            }

            foreach (var v in unitOfWorkWithConcurrentDictionary.Inserts())
            {
                Nop(v);
            }
        }


        [Benchmark]
        public void PopulateAndQueryUnitOfWorkWithImHashMap()
        {
            foreach (var v in values)
            {
                unitOfWorkWithImHashMap.StoreInserts(v);
            }

            foreach (var v in unitOfWorkWithImHashMap.Inserts())
            {
                Nop(v);
            }
        }
    }
}