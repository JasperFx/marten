using System;
using EventSourcingTests.Aggregation;
using Marten.Events;
using Marten.Testing.Harness;
using Newtonsoft.Json;
using Shouldly;
using Xunit;

namespace EventSourcingTests;

public class event_wrapper_must_be_serializable
{
    [Fact]
    public void can_round_trip()
    {
        // This was done for Wolverine

        var e = new Event<AEvent>(new AEvent());
        e.Id = Guid.NewGuid();
        e.Version = 3;
        e.Timestamp = DateTimeOffset.UtcNow;

        var json = JsonConvert.SerializeObject(e);

        var e2 = JsonConvert.DeserializeObject<Event<AEvent>>(json);

        e2.Id.ShouldBe(e.Id);
        e2.Data.ShouldNotBeNull();
    }
}
