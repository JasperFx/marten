using JasperFx.Events;
using Marten.Events;
using Marten.Exceptions;
using Shouldly;
using Xunit;

namespace CoreTests.Exceptions;

// #5337: Marten.Exceptions.ApplyEventException is [Obsolete] (superseded by
// JasperFx.Events.Daemon.ApplyEventException) but stays through 9.x for compat;
// this test pins the no-event-data message shape for as long as the type exists.
#pragma warning disable CS0618

public class ApplyEventExceptionTests
{
    public class FakeEventThatContainsSecretInformation
    {
        public string Secret { get; set; }
    }

    [Fact]
    public void should_only_include_sequence_and_id_no_data()
    {
        var @event = new Event<FakeEventThatContainsSecretInformation>(new()
        {
            Secret = "very secret!!!"
        });
        var exception = new ApplyEventException(@event, new("inner"));

        exception.Message.ShouldBe($"Failure to apply event #{@event.Sequence} Id({@event.Id})");
    }
}
