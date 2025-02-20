using System;
using EventSourcingTests.Aggregation;
using JasperFx.Events;
using Marten.Events;
using Shouldly;
using StronglyTypedIds;
using Xunit;

namespace EventSourcingTests.Projections;

public class identity_from_event
{
    [Fact]
    public void by_string()
    {
        var e = new Event<AEvent>(new AEvent()) { StreamKey = "foo" };

        e.IdentityFromEvent<string>(StreamIdentity.AsString)
            .ShouldBe("foo");
    }

    [Fact]
    public void by_guid()
    {
        var streamId = Guid.NewGuid();
        var e = new Event<AEvent>(new AEvent()) { StreamId = streamId };

        e.IdentityFromEvent<Guid>(StreamIdentity.AsGuid)
            .ShouldBe(streamId);
    }

    [Fact]
    public void by_strong_typed_guid()
    {
        var streamId = Guid.NewGuid();
        var e = new Event<AEvent>(new AEvent()) { StreamId = streamId };

        e.IdentityFromEvent<TripId>(StreamIdentity.AsGuid)
            .Value.ShouldBe(streamId);
    }

    [Fact]
    public void by_strong_typed_string()
    {
        var e = new Event<AEvent>(new AEvent()) { StreamKey = "bar" };

        e.IdentityFromEvent<EventSourcingTests.StringId>(StreamIdentity.AsString)
            .Value.ShouldBe("bar");
    }


}

[StronglyTypedId(Template.Guid)]
public partial struct TripId;

[StronglyTypedId(Template.String)]
public partial struct StringId;
