using System;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Microsoft.Extensions.Logging;
using Shouldly;
using StronglyTypedIds;
using Xunit;
using Xunit.Abstractions;

namespace EventSourcingTests.Aggregation;

// Sample code taken from https://github.com/JasperFx/marten/issues/3306
public class using_string_based_strong_typed_id_for_aggregate_identity: OneOffConfigurationsContext
{
    private readonly ITestOutputHelper _output;

    public using_string_based_strong_typed_id_for_aggregate_identity(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task can_utilize_strong_typed_id_in_aggregate_stream()
    {
        StoreOptions(opts =>
        {
            opts.UseSystemTextJsonForSerialization(new JsonSerializerOptions { IncludeFields = true });
            opts.Events.StreamIdentity = StreamIdentity.AsString;
        });

        var id = Guid.NewGuid().ToString();

        theSession.Events.StartStream<Payment2>(id, new PaymentCreated(DateTimeOffset.UtcNow),
            new PaymentVerified(DateTimeOffset.UtcNow));

        await theSession.SaveChangesAsync();

        var payment = await theSession.Events.AggregateStreamAsync<Payment2>(id);

        payment.Id.Value.Value.ShouldBe(id);
    }

    [Theory]
    [InlineData(ProjectionLifecycle.Inline)]
    [InlineData(ProjectionLifecycle.Async)]
    [InlineData(ProjectionLifecycle.Live)]
    public async Task use_fetch_for_writing(ProjectionLifecycle lifecycle)
    {
        StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.UseSystemTextJsonForSerialization(new JsonSerializerOptions { IncludeFields = true });
            opts.Projections.Add(new SingleStreamProjection<Payment2>(), lifecycle);
        });

        var id = Guid.NewGuid().ToString();
        theSession.Events.StartStream<Payment2>(id, new PaymentCreated(DateTimeOffset.UtcNow),
            new PaymentVerified(DateTimeOffset.UtcNow));

        await theSession.SaveChangesAsync();

        // This shouldn't blow up
        var stream = await theSession.Events.FetchForWriting<Payment2>(id);
        stream.Aggregate.Id.Value.Value.ShouldBe(id);
    }

    [Fact]
    public async Task can_utilize_strong_typed_id_in_with_inline_aggregations()
    {
        StoreOptions(opts =>
        {
            opts.UseSystemTextJsonForSerialization(new JsonSerializerOptions { IncludeFields = true });
            opts.Projections.Snapshot<Payment2>(SnapshotLifecycle.Inline);
            opts.Events.StreamIdentity = StreamIdentity.AsString;
        });

        var id = Guid.NewGuid().ToString();

        theSession.Events.StartStream<Payment2>(id, new PaymentCreated(DateTimeOffset.UtcNow),
            new PaymentVerified(DateTimeOffset.UtcNow));

        await theSession.SaveChangesAsync();

        var payment = await theSession.LoadAsync<Payment2>(new Payment2Id(id));

        payment.State.ShouldBe(PaymentState.Verified);
    }

    [Fact]
    public async Task can_utilize_strong_typed_id_with_async_aggregation()
    {
        var testLogger = new TestLogger<IProjection>(_output);
        StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.UseSystemTextJsonForSerialization(new JsonSerializerOptions { IncludeFields = true });
            opts.Projections.Snapshot<Payment2>(SnapshotLifecycle.Async);
            opts.DotNetLogger = testLogger;
        });

        var id = Guid.NewGuid().ToString();

        theSession.Events.StartStream<Payment2>(id, new PaymentCreated(DateTimeOffset.UtcNow),
            new PaymentVerified(DateTimeOffset.UtcNow));

        await theSession.SaveChangesAsync();


        using var daemon = await theStore.BuildProjectionDaemonAsync(logger: testLogger);
        await daemon.StartAllAsync();
        await daemon.WaitForNonStaleData(1.Minutes());

        var payment = await theSession.LoadAsync<Payment2>(new Payment2Id(id));

        payment.State.ShouldBe(PaymentState.Verified);


        // Do it again so you catch existing aggregates
        theSession.Events.Append(id, new PaymentCanceled(DateTimeOffset.UtcNow));
        await theSession.SaveChangesAsync();

        await daemon.WaitForNonStaleData(1.Minutes());

        payment = await theSession.LoadAsync<Payment2>(new Payment2Id(id));

        payment.State.ShouldBe(PaymentState.Canceled);
    }
}

[StronglyTypedId(Template.String)]
public readonly partial struct Payment2Id;

public class Payment2
{
    [JsonInclude] public Payment2Id? Id { get; private set; }

    [JsonInclude] public DateTimeOffset CreatedAt { get; private set; }

    [JsonInclude] public PaymentState State { get; private set; }

    public static Payment2 Create(IEvent<PaymentCreated> @event)
    {
        return new Payment2
        {
            Id = new Payment2Id(@event.StreamKey), CreatedAt = @event.Data.CreatedAt, State = PaymentState.Created
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

