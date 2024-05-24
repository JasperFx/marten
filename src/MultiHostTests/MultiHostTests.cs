using Npgsql;

namespace MultiHostTests;

public class MultiHostTests : MultiHostConfigurationContext
{

    [Fact]
    public async Task QueryHitsReplicaWhenConfigured()
    {
        var result = await theSession.QueryAsync<bool>("SELECT pg_is_in_recovery();");
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

        var result = await theSession.QueryAsync<bool>("SELECT pg_is_in_recovery();");
        var isReplica = result[0];

        Assert.False(isReplica);
    }
}
