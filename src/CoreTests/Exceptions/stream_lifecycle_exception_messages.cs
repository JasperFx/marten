using System;
using Shouldly;
using Xunit;

namespace CoreTests.Exceptions;

/// <summary>
///     #5475. Both messages used to state the failure and stop there. These facts pin the remedy
///     half so a later edit cannot quietly drop it. marten#5847: Marten now throws the JasperFx.Events
///     types, so these pin the shared messages.
/// </summary>
public class stream_lifecycle_exception_messages
{
    [Fact]
    public void collision_message_keeps_the_id_and_names_the_alternatives()
    {
        var id = Guid.NewGuid();
        var ex = new ExistingStreamIdCollisionException(id, typeof(string));

        ex.Message.ShouldStartWith($"Stream with id '{id}' already exists.");
        ex.Message.ShouldContain("StartStream requires a new id");
        ex.Message.ShouldContain("Append");
        ex.Message.ShouldContain("FetchForWriting");
        ex.Id.ShouldBe(id);
        ex.AggregateType.ShouldBe(typeof(string));
    }

    [Fact]
    public void nonexistent_stream_message_keeps_the_id_and_names_the_two_ways_in()
    {
        var ex = new NonExistentStreamException("quest-1");

        ex.Message.ShouldStartWith("Attempt to append to a nonexistent event stream 'quest-1'.");
        ex.Message.ShouldContain("AppendOptimistic");
        ex.Message.ShouldContain("AppendExclusive");
        ex.Message.ShouldContain("StartStream");
        ex.Id.ShouldBe("quest-1");
    }
}
