using JasperFx.Events.Daemon;
using Marten;
using Marten.Events.Projections;
using Shouldly;
using Xunit;

namespace DaemonTests.Resiliency;

public class error_handling_defaults
{
    [Fact]
    public void default_policies()
    {
        var options = new ProjectionOptions(new StoreOptions());

        // marten#6003 (10.0): a failing identity parks, and the shard pauses where it can't park
        options.Errors.ApplyErrors.ShouldBe(ProjectionErrorAction.Park);
        options.Errors.SerializationErrors.ShouldBe(ProjectionErrorAction.Park);
        options.Errors.SkipApplyErrors.ShouldBeFalse();
        options.Errors.SkipSerializationErrors.ShouldBeFalse();
        options.Errors.SkipUnknownEvents.ShouldBeTrue();

        options.RebuildErrors.SkipApplyErrors.ShouldBeFalse();
        options.RebuildErrors.SkipSerializationErrors.ShouldBeFalse();
        options.RebuildErrors.SkipUnknownEvents.ShouldBeFalse();
    }
}
