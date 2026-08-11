using Npgsql;

namespace MultiHostTests;

public class MultiHostTests : MultiHostConfigurationContext
{
    // Both tests query through a QUERY session, not the writable one they used before #5096.
    //
    // ReadSessionPreference only routes a connection opened with ConnectionUsage.Read (see
    // MartenDatabase.CreateConnection); a LightweightSession is a write session and takes
    // WriteSessionPreference — Primary — whatever the read preference says. So the first test
    // asserted pg_is_in_recovery() on the primary and could never pass, and the second passed
    // vacuously: it reached the primary because it was a write session, not because it had just
    // set ReadSessionPreference to Primary. Neither was noticed because this project was in no
    // solution and no CI job.

    [Fact]
    public async Task QueryHitsReplicaWhenConfigured()
    {
        await using var session = theStore.QuerySession();

        var result = await session.QueryAsync<bool>("SELECT pg_is_in_recovery();");
        var isReplica = result[0];

        Assert.True(isReplica);
    }

    [Fact]
    public async Task QueryHitsPrimaryWhenConfigured()
    {
        StoreOptions(x =>
        {
            x.Advanced.MultiHostSettings.ReadSessionPreference = TargetSessionAttributes.Primary;
        });

        await using var session = theStore.QuerySession();

        var result = await session.QueryAsync<bool>("SELECT pg_is_in_recovery();");
        var isReplica = result[0];

        Assert.False(isReplica);
    }
}
