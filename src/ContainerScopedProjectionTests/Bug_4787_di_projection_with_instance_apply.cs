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
/// and conventional instance methods (<c>Create</c> / <c>Apply</c> /
/// <c>ShouldDelete</c>) has its dependencies silently NULL inside every
/// convention method on Marten 9.11+ / JasperFx.Events 2.14.x+. The
/// reporter's original "silent zero events" symptom came from a downstream
/// null-conditional masking the NRE; these tests deref the injected
/// service directly so the failure surfaces deterministically.
///
/// Root cause is visible in the generated Evolver (file is in
/// <c>obj/.../OrderProjection.Evolver.g.cs</c>):
/// <code>
/// _projection => _projectionInstance ??= (OrderProjection)
///     RuntimeHelpers.GetUninitializedObject(typeof(OrderProjection));
/// </code>
/// That bypasses the constructor entirely — added in jasperfx#470 /
/// JasperFx 2.14.1 commit <c>1b728d1</c>
/// (<c>EvolverCodeEmitter.cs:152-163</c>) to make DI-only-ctor projections
/// compile (previously CS7036). It fixed compilation but introduced this
/// runtime regression: <c>tryUseAssemblyRegisteredEvolver</c>
/// (<c>JasperFxAggregationProjectionBase.cs:222</c>) routes every Apply /
/// Create / ShouldDelete through the uninitialized shadow, so the
/// DI-resolved projection that <c>AddProjectionWithServices</c> supplied
/// is never used for event dispatch.
///
/// Coverage matrix: 3 convention methods × 3 <see cref="ServiceLifetime"/>
/// values = 9 cases, all expected to fail today the same way and to pass
/// once the dispatcher routes through the DI-built instance.
///
/// Note that <c>AddProjectionWithServices</c> documents Transient as
/// "treated as Scoped" — we still parameterize over all three so any
/// future per-lifetime divergence is captured.
/// </summary>
[Collection("ioc")]
public class Bug_4787_di_projection_with_instance_apply
{
    [Theory]
    [InlineData(ServiceLifetime.Singleton)]
    [InlineData(ServiceLifetime.Scoped)]
    [InlineData(ServiceLifetime.Transient)]
    public async Task Create_convention_sees_injected_dependency(ServiceLifetime lifetime)
    {
        var recorder = await runScenario("create", lifetime, append =>
        {
            // Stream-start event triggers the Create convention.
            append(new OrderCreatedEvent { OrderNumber = "X", Version = 1 });
        });

        recorder.Created.Count.ShouldBe(1);
        recorder.Created[0].ShouldBeOfType<OrderCreatedEvent>();
    }

    [Theory]
    [InlineData(ServiceLifetime.Singleton)]
    [InlineData(ServiceLifetime.Scoped)]
    [InlineData(ServiceLifetime.Transient)]
    public async Task Apply_convention_sees_injected_dependency(ServiceLifetime lifetime)
    {
        var recorder = await runScenario("apply", lifetime, append =>
        {
            // Create first to materialize the snapshot, then Apply runs for
            // the second event.
            append(new OrderCreatedEvent { OrderNumber = "X", Version = 1 });
            append(new OrderUpdatedEvent { OrderNumber = "X", Version = 2, Total = 42m });
        });

        recorder.Applied.Count.ShouldBe(1);
        recorder.Applied[0].ShouldBeOfType<OrderUpdatedEvent>();
    }

    [Theory]
    [InlineData(ServiceLifetime.Singleton)]
    [InlineData(ServiceLifetime.Scoped)]
    [InlineData(ServiceLifetime.Transient)]
    public async Task ShouldDelete_convention_sees_injected_dependency(ServiceLifetime lifetime)
    {
        var recorder = await runScenario("delete", lifetime, append =>
        {
            // Create + Closed sequence — ShouldDelete is the predicate that
            // tombstones the aggregate when a Closed event arrives.
            append(new OrderCreatedEvent { OrderNumber = "X", Version = 1 });
            append(new OrderClosedEvent { OrderNumber = "X", Version = 2 });
        });

        recorder.ShouldDeleteChecked.Count.ShouldBe(1);
        recorder.ShouldDeleteChecked[0].ShouldBeOfType<OrderClosedEvent>();
    }

    private static async Task<OrderEventRecorder> runScenario(
        string scenario, ServiceLifetime lifetime, Action<Action<OrderEvent>> arrange)
    {
        // Per-scenario, per-lifetime schema so test cases don't collide.
        // Compact name to stay well under PostgreSQL's 63-char identifier limit.
        var schema = $"b4787_{scenario}_{lifetime.ToString().ToLowerInvariant()}";

        await using (var conn = new NpgsqlConnection(ConnectionSource.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.DropSchemaAsync(schema);
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
                        opts.DatabaseSchemaName = schema;
                        opts.Events.StreamIdentity = StreamIdentity.AsString;
                        opts.Schema.For<OrderAggregate>().Identity(x => x.OrderNumber);
                    })
                    .AddProjectionWithServices<OrderProjection>(ProjectionLifecycle.Inline, lifetime);
            })
            .StartAsync();

        var store = host.Services.GetRequiredService<IDocumentStore>();

        var events = new List<OrderEvent>();
        arrange(events.Add);

        await using (var session = store.LightweightSession())
        {
            session.Events.StartStream<OrderAggregate>("X", events.Cast<object>().ToArray());
            await session.SaveChangesAsync();
        }

        return recorder;
    }
}

public interface IOrderEventRecorder
{
    void RecordCreate(OrderEvent @event);
    void RecordApply(OrderEvent @event);
    void RecordShouldDelete(OrderEvent @event);
    IReadOnlyList<OrderEvent> Created { get; }
    IReadOnlyList<OrderEvent> Applied { get; }
    IReadOnlyList<OrderEvent> ShouldDeleteChecked { get; }
}

public class OrderEventRecorder: IOrderEventRecorder
{
    private readonly List<OrderEvent> _created = new();
    private readonly List<OrderEvent> _applied = new();
    private readonly List<OrderEvent> _shouldDelete = new();

    public void RecordCreate(OrderEvent @event) => _created.Add(@event);
    public void RecordApply(OrderEvent @event) => _applied.Add(@event);
    public void RecordShouldDelete(OrderEvent @event) => _shouldDelete.Add(@event);

    public IReadOnlyList<OrderEvent> Created => _created;
    public IReadOnlyList<OrderEvent> Applied => _applied;
    public IReadOnlyList<OrderEvent> ShouldDeleteChecked => _shouldDelete;
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

public class OrderClosedEvent: OrderEvent
{
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
/// convention methods (<c>Create</c>, <c>Apply</c>, <c>ShouldDelete</c>),
/// constructor-injected dependency. Every convention method dereferences
/// the injected recorder so a null-injection bug surfaces deterministically
/// (rather than silently as in the reporter's original null-conditional
/// masking).
/// </summary>
public partial class OrderProjection: SingleStreamProjection<OrderAggregate, string>
{
    private readonly IOrderEventRecorder _recorder;

    public OrderProjection(IOrderEventRecorder recorder)
    {
        _recorder = recorder;
    }

    public OrderAggregate Create(OrderCreatedEvent @event)
    {
        _recorder.RecordCreate(@event);

        return new OrderAggregate
        {
            OrderNumber = @event.OrderNumber,
            Versions = new List<OrderLine>
            {
                new() { OrderNumber = @event.OrderNumber, Version = @event.Version }
            }
        };
    }

    public OrderAggregate Apply(OrderAggregate snapshot, OrderUpdatedEvent @event)
    {
        _recorder.RecordApply(@event);

        var versions = snapshot.Versions.ToList();
        versions.Add(new OrderLine
        {
            OrderNumber = @event.OrderNumber,
            Version = @event.Version,
            Total = @event.Total
        });

        return snapshot with { Versions = versions.AsReadOnly() };
    }

    public bool ShouldDelete(OrderAggregate snapshot, OrderClosedEvent @event)
    {
        _recorder.RecordShouldDelete(@event);
        return true;
    }
}
