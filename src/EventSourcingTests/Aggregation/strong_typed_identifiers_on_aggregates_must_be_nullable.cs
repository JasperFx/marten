using System;
using System.Text.Json.Serialization;
using JasperFx.Events;
using Marten.Events;
using Marten.Events.Projections;
using Marten.Exceptions;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Aggregation;

public class Payment3
{
    [JsonInclude] public PaymentId Id { get; private set; }

    [JsonInclude] public DateTimeOffset CreatedAt { get; private set; }

    [JsonInclude] public PaymentState State { get; private set; }

    public static Payment3 Create(IEvent<PaymentCreated> @event)
    {
        return new Payment3
        {
            Id = new PaymentId(@event.StreamId), CreatedAt = @event.Data.CreatedAt, State = PaymentState.Created
        };
    }

    public void Apply(PaymentCanceled @event)
    {
        State = PaymentState.Canceled;
    }

    public void Apply(PaymentVerified @event)
    {
        State = PaymentState.Verified;
    }
}
