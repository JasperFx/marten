using Marten.Events.Daemon;
using Marten.Events.Projections;
using Marten.Subscriptions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Marten.AsyncDaemon.Testing;

public class configuration_logic
{
    [Fact]
    public void AsyncProjectionShard_Role_is_Projection()
    {
        var shard = new AsyncProjectionShard(Substitute.For<IProjectionSource>());
        shard.Role.ShouldBe(ShardRole.Projection);
    }

    [Fact]
    public void AsyncProjectionShard_Role_is_Subscription()
    {
        var source = Substitute.For<ISubscriptionSource>();
        var shard = new AsyncProjectionShard("Foo:All", source);
        shard.Role.ShouldBe(ShardRole.Subscription);
    }
}
