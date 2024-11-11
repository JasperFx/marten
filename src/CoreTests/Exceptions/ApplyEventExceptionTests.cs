using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten.Events;
using Marten.Exceptions;
using Shouldly;
using Xunit;

namespace CoreTests.Exceptions;

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
