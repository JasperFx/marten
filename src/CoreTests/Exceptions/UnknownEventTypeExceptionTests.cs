using Marten.Exceptions;
using Shouldly;
using Xunit;

namespace CoreTests.Exceptions;

public class UnknownEventTypeExceptionTests
{
    /// <summary>
    ///     #5477. The closing quote and the period used to be transposed, so the message read
    ///     <c>alias 'foo.' You may need ...</c> and anyone copying the alias out of the message
    ///     picked up a trailing period that is not part of the alias.
    /// </summary>
    [Fact]
    public void the_alias_is_quoted_before_the_sentence_ends()
    {
        var ex = new UnknownEventTypeException("trip_started");

        ex.Message.ShouldStartWith("Unknown event type name alias 'trip_started'. ");
        ex.Message.ShouldNotContain("'trip_started.'");
    }

    [Fact]
    public void names_both_remedies()
    {
        var ex = new UnknownEventTypeException("trip_started");

        ex.Message.ShouldContain("StoreOptions.Events.AddEventType(type)");
        ex.Message.ShouldContain("StoreOptions.Projections.Errors.SkipUnknownEvents");
    }

    [Fact]
    public void carries_the_alias_and_the_default_sequence()
    {
        var ex = new UnknownEventTypeException("trip_started");

        ex.EventTypeName.ShouldBe("trip_started");
        ex.Sequence.ShouldBe(UnknownEventTypeException.UnknownSequence);
    }
}
