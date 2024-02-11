using System;

namespace MartenBenchmarks.BenchAgainst;

public sealed class BenchModel4
{
    public BenchModel4()
    {
        Id = Random.Shared.Next(int.MaxValue);
    }

    public int Id { get; set; }
}
