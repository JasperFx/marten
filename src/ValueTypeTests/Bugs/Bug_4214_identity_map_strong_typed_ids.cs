using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Shouldly;
using StronglyTypedIds;
using Xunit;

namespace ValueTypeTests.Bugs;

public class Bug_4214_identity_map_strong_typed_ids : BugIntegrationContext
{
    [Theory]
    [InlineData(ProjectionLifecycle.Inline)]
    [InlineData(ProjectionLifecycle.Live)]
    public async Task fetch_for_writing_with_identity_map_and_strong_typed_guid_id(ProjectionLifecycle lifecycle)
    {
        StoreOptions(opts =>
        {
            opts.UseSystemTextJsonForSerialization(new JsonSerializerOptions { IncludeFields = true });
            opts.Projections.Add(new SingleStreamProjection<Bug4214Payment, Bug4214PaymentId>(), lifecycle);
            opts.Events.UseIdentityMapForAggregates = true;
        });

        await using var session = theStore.LightweightSession();

        var id = session.Events.StartStream<Bug4214Payment>(
            new Bug4214PaymentCreated(DateTimeOffset.UtcNow),
            new Bug4214PaymentVerified(DateTimeOffset.UtcNow)).Id;

        await session.SaveChangesAsync();

        // This threw InvalidCastException before the fix:
        // "Unable to cast object of type 'Dictionary`2[Bug4214PaymentId,Bug4214Payment]'
        // to type 'Dictionary`2[Guid,Bug4214Payment]'"
        var stream = await session.Events.FetchForWriting<Bug4214Payment>(id);
        stream.Aggregate.ShouldNotBeNull();
        stream.Aggregate.State.ShouldBe(Bug4214PaymentState.Verified);
    }

    [Theory]
    [InlineData(ProjectionLifecycle.Inline)]
    [InlineData(ProjectionLifecycle.Live)]
    public async Task fetch_for_writing_twice_with_identity_map_and_strong_typed_guid_id(
        ProjectionLifecycle lifecycle)
    {
        StoreOptions(opts =>
        {
            opts.UseSystemTextJsonForSerialization(new JsonSerializerOptions { IncludeFields = true });
            opts.Projections.Add(new SingleStreamProjection<Bug4214Payment, Bug4214PaymentId>(), lifecycle);
            opts.Events.UseIdentityMapForAggregates = true;
        });

        await using var session = theStore.LightweightSession();

        var id = session.Events.StartStream<Bug4214Payment>(
            new Bug4214PaymentCreated(DateTimeOffset.UtcNow),
            new Bug4214PaymentVerified(DateTimeOffset.UtcNow)).Id;

        await session.SaveChangesAsync();

        // First fetch stores in identity map
        var stream1 = await session.Events.FetchForWriting<Bug4214Payment>(id);
        stream1.Aggregate.ShouldNotBeNull();

        stream1.AppendOne(new Bug4214PaymentCanceled(DateTimeOffset.UtcNow));
        await session.SaveChangesAsync();

        // Second fetch should retrieve from identity map without cast error
        var stream2 = await session.Events.FetchForWriting<Bug4214Payment>(id);
        stream2.Aggregate.ShouldNotBeNull();
        stream2.Aggregate.State.ShouldBe(Bug4214PaymentState.Canceled);
    }
}

[StronglyTypedId(Template.Guid)]
public readonly partial struct Bug4214PaymentId;

public class Bug4214Payment
{
    [JsonInclude] public Bug4214PaymentId? Id { get; private set; }
    [JsonInclude] public DateTimeOffset CreatedAt { get; private set; }
    [JsonInclude] public Bug4214PaymentState State { get; private set; }

    public static Bug4214Payment Create(IEvent<Bug4214PaymentCreated> @event)
    {
        return new Bug4214Payment
        {
            Id = new Bug4214PaymentId(@event.StreamId),
            CreatedAt = @event.Data.CreatedAt,
            State = Bug4214PaymentState.Created
        };
    }

    public void Apply(Bug4214PaymentVerified _) => State = Bug4214PaymentState.Verified;
    public void Apply(Bug4214PaymentCanceled _) => State = Bug4214PaymentState.Canceled;
}

public enum Bug4214PaymentState { Created, Verified, Canceled }

public record Bug4214PaymentCreated(DateTimeOffset CreatedAt);
public record Bug4214PaymentVerified(DateTimeOffset VerifiedAt);
public record Bug4214PaymentCanceled(DateTimeOffset CanceledAt);
