using System;

namespace MartenBenchmarks.BenchAgainst
{
    public sealed class BenchModel
    {
        public BenchModel()
        {
            Id = Guid.NewGuid();
        }

        public Guid Id { get; set; }
    }
}