using System;
using Marten.Exceptions;
using Shouldly;
using Xunit;

namespace CoreTests.Exceptions;

/// <summary>
///     #5475. Both messages used to state the failure and stop there. These facts pin the remedy
///     half so a later edit cannot quietly drop it.
/// </summary>
public class stream_lifecycle_exception_messages
{
    [Fact]
    public void collision_message_keeps_the_id_and_names_the_alternatives()
    {
        var id = Guid.NewGuid();
        var ex = new ExistingStreamIdCollisionException(id, typeof(string));

        ex.Message.ShouldStartWith($"Stream #{id} already exists in the database.");
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
        ex.Message.ShouldContain("UseMandatoryStreamTypeDeclaration");
        ex.Id.ShouldBe("quest-1");
    }
}
