using System;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using NSubstitute;
using Xunit;

namespace EventSourcingTests.Projections;

public class inline_aggregation_with_custom_projection_configuration : OneOffConfigurationsContext
{
    [Fact]
    public void does_call_custom_projection_configuration()
    {
        var configureProjection = Substitute.For<Action<SingleStreamProjection<QuestParty>>>();

        StoreOptions(_ =>
        {
            _.Projections.Snapshot<QuestParty>(SnapshotLifecycle.Inline, configureProjection);
        });

        configureProjection.Received(1).Invoke(Arg.Any<SingleStreamProjection<QuestParty>>());
    }
}
