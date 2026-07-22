using Xunit;

namespace Marten.TimescaleDB.Tests;

/// <summary>
/// Serializes the TimescaleDB test classes so concurrent schema application against a single
/// database instance doesn't race (mirrors the DISABLE_TEST_PARALLELIZATION CI setting).
/// </summary>
[CollectionDefinition("timescaledb", DisableParallelization = true)]
public class TimescaleDBCollection
{
}
