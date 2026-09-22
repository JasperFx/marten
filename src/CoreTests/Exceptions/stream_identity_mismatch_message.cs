using Marten.Events;
using Shouldly;
using Xunit;

namespace CoreTests.Exceptions;

/// <summary>
///     #5474. The mismatch message used to state only the fact ("... is configured to identify
///     streams with Guids"), leaving the reader to discover both the setting that decides it and the
///     overloads to call instead. These facts pin the two things that make the message actionable so
///     a later edit cannot quietly drop them.
/// </summary>
public class stream_identity_mismatch_message
{
    [Fact]
    public void guid_message_names_the_setting_and_the_overloads()
    {
        EventGraph.GuidIdentityMismatchMessage.ShouldContain("StreamIdentity.AsGuid");
        EventGraph.GuidIdentityMismatchMessage.ShouldContain("opts.Events.StreamIdentity");
        EventGraph.GuidIdentityMismatchMessage.ShouldContain("Guid stream id overloads");
        EventGraph.GuidIdentityMismatchMessage.ShouldContain("StreamIdentity.AsString");
    }

    [Fact]
    public void string_message_names_the_setting_and_the_overloads()
    {
        EventGraph.StringIdentityMismatchMessage.ShouldContain("StreamIdentity.AsString");
        EventGraph.StringIdentityMismatchMessage.ShouldContain("opts.Events.StreamIdentity");
        EventGraph.StringIdentityMismatchMessage.ShouldContain("string stream key overloads");
        EventGraph.StringIdentityMismatchMessage.ShouldContain("StreamIdentity.AsGuid");
    }
}
