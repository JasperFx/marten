using System;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using EventSourcingTests.Projections;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten;
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
public class using_guid_based_strong_typed_id_for_aggregate_identity: OneOffConfigurationsContext
{
    private readonly ITestOutputHelper _output;

    public using_guid_based_strong_typed_id_for_aggregate_identity(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task can_utilize_strong_typed_id_in_aggregate_stream()
    {
        StoreOptions(opts =>
        {
            opts.UseSystemTextJsonForSerialization(new JsonSerializerOptions { IncludeFields = true });
        });

        var id = theSession.Events.StartStream<Payment>(new PaymentCreated(DateTimeOffset.UtcNow),
            new PaymentVerified(DateTimeOffset.UtcNow)).Id;

        await theSession.SaveChangesAsync();

        var payment = await theSession.Events.AggregateStreamAsync<Payment>(id);

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
            opts.UseSystemTextJsonForSerialization(new JsonSerializerOptions { IncludeFields = true });
            opts.Projections.Add(new SingleStreamProjection<Payment>(), lifecycle);
        });

        var id = theSession.Events.StartStream<Payment>(new PaymentCreated(DateTimeOffset.UtcNow),
            new PaymentVerified(DateTimeOffset.UtcNow)).Id;

        await theSession.SaveChangesAsync();

        // This shouldn't blow up
        var stream = await theSession.Events.FetchForWriting<Payment>(id);
        stream.Aggregate.Id.Value.Value.ShouldBe(id);
    }

    [Fact]
    public async Task can_utilize_strong_typed_id_in_with_inline_aggregations()
    {
        StoreOptions(opts =>
        {
            opts.UseSystemTextJsonForSerialization(new JsonSerializerOptions { IncludeFields = true });
            opts.Projections.Snapshot<Payment>(SnapshotLifecycle.Inline);
        });

        var id = theSession.Events.StartStream<Payment>(new PaymentCreated(DateTimeOffset.UtcNow),
            new PaymentVerified(DateTimeOffset.UtcNow)).Id;

        await theSession.SaveChangesAsync();

        var payment = await theSession.LoadAsync<Payment>(new PaymentId(id));

        payment.State.ShouldBe(PaymentState.Verified);
    }

    #region sample_use_fetch_for_writing_with_strong_typed_identifier

    private async Task use_fetch_for_writing_with_strong_typed_identifier(PaymentId id, IDocumentSession session)
    {
        var stream = await session.Events.FetchForWriting<Payment>(id.Value);
    }

    #endregion

    [Fact]
    public async Task can_utilize_strong_typed_id_with_async_aggregation()
    {
        var testLogger = new TestLogger<IProjection>(_output);
        StoreOptions(opts =>
        {
            opts.UseSystemTextJsonForSerialization(new JsonSerializerOptions { IncludeFields = true });
            opts.Projections.Snapshot<Payment>(SnapshotLifecycle.Async);
            opts.DotNetLogger = testLogger;
        });

        var id = theSession.Events.StartStream<Payment>(new PaymentCreated(DateTimeOffset.UtcNow),
            new PaymentVerified(DateTimeOffset.UtcNow)).Id;

        await theSession.SaveChangesAsync();


        using var daemon = await theStore.BuildProjectionDaemonAsync(logger: testLogger);
        await daemon.StartAllAsync();
        await daemon.WaitForNonStaleData(1.Minutes());

        var payment = await theSession.LoadAsync<Payment>(new PaymentId(id));

        payment.State.ShouldBe(PaymentState.Verified);


        // Do it again so you catch existing aggregates
        theSession.Events.Append(id, new PaymentCanceled(DateTimeOffset.UtcNow));
        await theSession.SaveChangesAsync();

        await daemon.WaitForNonStaleData(1.Minutes());

        payment = await theSession.LoadAsync<Payment>(new PaymentId(id));

        payment.State.ShouldBe(PaymentState.Canceled);
    }
}

#region sample_using_strong_typed_identifier_for_aggregate_projections

[StronglyTypedId(Template.Guid)]
public readonly partial struct PaymentId;

public class Payment
{
    [JsonInclude] public PaymentId? Id { get; private set; }

    [JsonInclude] public DateTimeOffset CreatedAt { get; private set; }

    [JsonInclude] public PaymentState State { get; private set; }

    public static Payment Create(IEvent<PaymentCreated> @event)
    {
        return new Payment
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

#endregion

public enum PaymentState
{
    Created,
    Initialized,
    Canceled,
    Verified
}

public record PaymentCreated(
    DateTimeOffset CreatedAt
);

public record PaymentCanceled(
    DateTimeOffset CanceledAt
);

public record PaymentVerified(
    DateTimeOffset VerifiedAt
);

public class TestLogger<T>: ILogger<T>, IDisposable
{
    private readonly ITestOutputHelper _output;

    public TestLogger(ITestOutputHelper output)
    {
        _output = output;
    }


    public void Dispose()
    {
        // Nothing
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception,
        Func<TState, Exception, string> formatter)
    {
        var message = $"{typeof(T).NameInCode()}/{logLevel}: {formatter(state, exception)}";
        Debug.WriteLine(message);
        _output.WriteLine(message);

        if (exception != null)
        {
            Debug.WriteLine(exception);
            _output.WriteLine(exception.ToString());
        }
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    public IDisposable BeginScope<TState>(TState state)
    {
        return this;
    }
}
