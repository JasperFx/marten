using System;

namespace MartenBenchmarks.BenchAgainst
{
    public sealed class BenchModel2
    {
        public BenchModel2()
        {
            Id = Guid.NewGuid();
        }

        public Guid Id { get; set; }
    }
}