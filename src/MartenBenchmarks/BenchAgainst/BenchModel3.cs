using System;

namespace MartenBenchmarks.BenchAgainst
{
    public sealed class BenchModel3
    {
        public BenchModel3()
        {
            Id = Guid.NewGuid();
        }

        public Guid Id { get; set; }
    }
}