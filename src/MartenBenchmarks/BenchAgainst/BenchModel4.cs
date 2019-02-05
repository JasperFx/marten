using System;

namespace MartenBenchmarks.BenchAgainst
{
    public sealed class BenchModel4
    {
        public BenchModel4()
        {
            Id = new Random().Next(int.MaxValue);
        }

        public int Id { get; set; }
    }
}