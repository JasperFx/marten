using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Aggregation;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace ContainerScopedProjectionTests;

/// <summary>
/// Repro for https://github.com/JasperFx/marten/issues/4787. A
/// <see cref="SingleStreamProjection{T,TId}"/> registered via
/// <c>AddProjectionWithServices</c> with constructor-injected dependencies
/// and an instance <c>Apply(aggregate, event)</c> method has its dependencies
/// silently NULL inside Apply on Marten 9.11+ / JasperFx.Events 2.14.x+.
/// The reporter's original symptom ("silent zero events") was their own
/// null-conditional <c>_sender?.SendAsync(...)</c> masking the bug; this
/// test dereferences the injected service directly so it surfaces
/// deterministically as a <c>NullReferenceException</c>.
///
/// Root cause is visible in the generated Evolver
/// (<c>obj/.../OrderProjection.Evolver.g.cs</c>), which builds its private
/// shadow projection instance via:
/// <code>
/// _projection => _projectionInstance ??= (OrderProjection)
///     RuntimeHelpers.GetUninitializedObject(typeof(OrderProjection));
/// </code>
/// That bypasses the constructor entirely — see jasperfx#470 / JasperFx
/// 2.14.1 commit 1b728d1 (<c>EvolverCodeEmitter.cs:152-163</c>), added to
/// make DI-only ctor projections compile (previously CS7036). The fallback
/// fixed compilation but introduced this runtime regression: the
/// DI-resolved projection that <c>AddProjectionWithServices</c> supplied
/// is never used for event dispatch — the assembly-registered Evolver
/// (selected by <c>JasperFxAggregationProjectionBase.tryUseAssemblyRegisteredEvolver</c>,
/// frame 222) routes every Apply through the uninitialized shadow.
///
/// Expected when fixed: the injected <c>IOrderEventRecorder</c> sees both
/// events, the test passes.
/// </summary>
[Collection("ioc")]
public class Bug_4787_di_projection_with_instance_apply
{
    [Fact]
    public async Task instance_apply_should_see_injected_dependency_when_registered_via_AddProjectionWithServices()
    {
        await using (var conn = new NpgsqlConnection(ConnectionSource.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.DropSchemaAsync("bug4787");
            await conn.CloseAsync();
        }

        var recorder = new OrderEventRecorder();

        using var host = await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<IOrderEventRecorder>(recorder);

                services.AddMarten(opts =>
                    {
                        opts.Connection(ConnectionSource.ConnectionString);
                        opts.DatabaseSchemaName = "bug4787";
                        opts.Events.StreamIdentity = StreamIdentity.AsString;
                        opts.Schema.For<OrderAggregate>().Identity(x => x.OrderNumber);
                    })
                    .AddProjectionWithServices<OrderProjection>(ProjectionLifecycle.Inline, ServiceLifetime.Scoped);
            })
            .StartAsync();

        var store = host.Services.GetRequiredService<IDocumentStore>();

        const string orderNumber = "ORDER-1";

        await using (var session = store.LightweightSession())
        {
            session.Events.StartStream<OrderAggregate>(orderNumber,
                new OrderCreatedEvent { OrderNumber = orderNumber, Version = 1 });
            await session.SaveChangesAsync();
        }

        await using (var session = store.LightweightSession())
        {
            session.Events.Append(orderNumber,
                new OrderUpdatedEvent { OrderNumber = orderNumber, Version = 2, Total = 42m });
            await session.SaveChangesAsync();
        }

        // The projected document updates correctly — this proves Apply fired.
        await using (var query = store.QuerySession())
        {
            var aggregate = await query.LoadAsync<OrderAggregate>(orderNumber);
            aggregate.ShouldNotBeNull();
            aggregate.OrderNumber.ShouldBe(orderNumber);
            aggregate.Versions.Count.ShouldBe(2);
            aggregate.Versions[1].Total.ShouldBe(42m);
        }

        // ...but the injected recorder must have seen each event.
        // If 4787 is live, the recorder's count is 0 (or the projection's
        // _recorder field was null inside Apply, NRE'd, and the test threw
        // before reaching here).
        recorder.RecordedEvents.Count.ShouldBe(2);
        recorder.RecordedEvents[0].ShouldBeOfType<OrderCreatedEvent>();
        recorder.RecordedEvents[1].ShouldBeOfType<OrderUpdatedEvent>();
    }
}

public interface IOrderEventRecorder
{
    void Record(OrderEvent @event);
    IReadOnlyList<OrderEvent> RecordedEvents { get; }
}

public class OrderEventRecorder: IOrderEventRecorder
{
    private readonly List<OrderEvent> _events = new();

    public void Record(OrderEvent @event) => _events.Add(@event);

    public IReadOnlyList<OrderEvent> RecordedEvents => _events;
}

public abstract class OrderEvent
{
    public required string OrderNumber { get; init; }
    public required int Version { get; init; }
}

public class OrderCreatedEvent: OrderEvent
{
}

public class OrderUpdatedEvent: OrderEvent
{
    public required decimal Total { get; init; }
}

public record OrderAggregate
{
    public string OrderNumber { get; init; } = string.Empty;
    public IReadOnlyList<OrderLine> Versions { get; init; } = new List<OrderLine>();
}

public record OrderLine
{
    public string OrderNumber { get; init; } = string.Empty;
    public int Version { get; init; }
    public decimal Total { get; init; }
}

/// <summary>
/// Mirrors the reporter's shape: partial projection class, instance
/// <c>Apply</c> methods, constructor-injected dependencies. The Apply
/// methods use the injected recorder so a null-injection bug surfaces
/// either as an NRE (loud) or as zero recorded events (quiet).
/// </summary>
public partial class OrderProjection: SingleStreamProjection<OrderAggregate, string>
{
    private readonly IOrderEventRecorder _recorder;

    public OrderProjection(IOrderEventRecorder recorder)
    {
        _recorder = recorder;
    }

    public OrderAggregate Apply(OrderAggregate aggregate, OrderCreatedEvent @event)
    {
        // Deref the injected dep on every event so a null _recorder
        // surfaces deterministically (rather than silently as in the
        // reporter's original null-conditional masking).
        _recorder.Record(@event);

        return aggregate with
        {
            OrderNumber = @event.OrderNumber,
            Versions = new List<OrderLine>
            {
                new() { OrderNumber = @event.OrderNumber, Version = @event.Version }
            }
        };
    }

    public OrderAggregate Apply(OrderAggregate aggregate, OrderUpdatedEvent @event)
    {
        _recorder.Record(@event);

        var versions = aggregate.Versions.ToList();
        // 1-based version index per the reporter's shape
        var idx = @event.Version - 1;
        if (idx < versions.Count)
        {
            versions[idx] = versions[idx] with { Total = @event.Total };
        }
        else
        {
            versions.Add(new OrderLine
            {
                OrderNumber = @event.OrderNumber,
                Version = @event.Version,
                Total = @event.Total
            });
        }

        return aggregate with { Versions = versions.AsReadOnly() };
    }
}
