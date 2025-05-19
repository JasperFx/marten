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
        options.Errors.SkipApplyErrors.ShouldBeTrue();
        options.Errors.SkipSerializationErrors.ShouldBeTrue();
        options.Errors.SkipUnknownEvents.ShouldBeTrue();

        options.RebuildErrors.SkipApplyErrors.ShouldBeFalse();
        options.RebuildErrors.SkipSerializationErrors.ShouldBeFalse();
        options.RebuildErrors.SkipUnknownEvents.ShouldBeFalse();
    }
}
