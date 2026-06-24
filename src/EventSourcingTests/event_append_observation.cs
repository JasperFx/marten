using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten;
using Marten.Events;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace EventSourcingTests;

public record DiagObservedA(string Note);

public record DiagObservedB(int Amount);

/// <summary>
/// #4782: Marten emits a runtime event-append observation through the storage-agnostic
/// JasperFx.Events <see cref="IEventStoreInstrumentation.AppendObserver"/> after each successful
/// SaveChanges, so lifecycle tooling (CritterWatch) can record runtime-observed "appends" edges.
/// </summary>
public class event_append_observation: OneOffConfigurationsContext
{
    [Fact]
    public async Task observer_receives_the_appended_events_with_metadata()
    {
        var observed = new List<IReadOnlyList<IEvent>>();

        var store = StoreOptions(opts => opts.EventGraph.AppendObserver = list => observed.Add(list));

        var streamId = Guid.NewGuid();
        await using var session = store.LightweightSession();
        session.Events.StartStream(streamId, new DiagObservedA("hello"), new DiagObservedB(3));
        await session.SaveChangesAsync();

        observed.Count.ShouldBe(1);
        var batch = observed.Single();
        batch.Count.ShouldBe(2);

        batch.ShouldAllBe(e => e.StreamId == streamId);
        batch.ShouldContain(e => e.Data is DiagObservedA);
        batch.ShouldContain(e => e.Data is DiagObservedB);
        batch.ShouldAllBe(e => !string.IsNullOrEmpty(e.EventTypeName));
        batch.ShouldAllBe(e => e.Timestamp != default);
    }

    [Fact]
    public async Task observer_carries_the_stream_key_for_string_identified_streams()
    {
        var observed = new List<IReadOnlyList<IEvent>>();

        var store = StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.EventGraph.AppendObserver = list => observed.Add(list);
        });

        await using var session = store.LightweightSession();
        session.Events.StartStream("metrics/widget-7", new DiagObservedA("k"));
        await session.SaveChangesAsync();

        var batch = observed.Single();
        batch.ShouldAllBe(e => e.StreamKey == "metrics/widget-7");
    }

    [Fact]
    public async Task observer_carries_the_tenant_id_when_multi_tenanted()
    {
        var observed = new List<IReadOnlyList<IEvent>>();

        var store = StoreOptions(opts =>
        {
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.EventGraph.AppendObserver = list => observed.Add(list);
        });

        await using var session = store.LightweightSession("acme");
        session.Events.StartStream(Guid.NewGuid(), new DiagObservedA("t"));
        await session.SaveChangesAsync();

        var batch = observed.Single();
        batch.ShouldAllBe(e => e.TenantId == "acme");
    }

    [Fact]
    public async Task observer_does_not_fire_when_no_events_are_appended()
    {
        var fired = false;

        var store = StoreOptions(opts => opts.EventGraph.AppendObserver = _ => fired = true);

        await using var session = store.LightweightSession();
        session.Store(new DiagDocForObservation { Id = Guid.NewGuid(), Name = "doc-only" });
        await session.SaveChangesAsync();

        fired.ShouldBeFalse();
    }

    [Fact]
    public async Task a_throwing_observer_does_not_break_save_changes()
    {
        var store = StoreOptions(opts =>
            opts.EventGraph.AppendObserver = _ => throw new InvalidOperationException("boom"));

        var streamId = Guid.NewGuid();
        await using var session = store.LightweightSession();
        session.Events.StartStream(streamId, new DiagObservedA("still committed"));

        // The observer is best-effort: its failure must not surface from SaveChanges.
        await Should.NotThrowAsync(async () => await session.SaveChangesAsync());

        // ...and the events were genuinely committed.
        await using var query = store.QuerySession();
        var events = await query.Events.FetchStreamAsync(streamId);
        events.Count.ShouldBe(1);
    }

    [Fact]
    public async Task no_observer_is_a_safe_no_op()
    {
        var store = StoreOptions(_ => { });

        await using var session = store.LightweightSession();
        session.Events.StartStream(Guid.NewGuid(), new DiagObservedA("no observer"));

        await Should.NotThrowAsync(async () => await session.SaveChangesAsync());
    }

    [Fact]
    public async Task observer_set_through_the_di_instrumentation_bridge_is_wired_to_the_store()
    {
        var observed = new List<IReadOnlyList<IEvent>>();

        var services = new ServiceCollection();
        services.AddMarten(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "event_append_obs_di";
        });

        await using var provider = services.BuildServiceProvider();

        // The consumer (e.g. a CritterWatch satellite) sets the observer on the storage-agnostic
        // instrumentation surface before the store is built.
        var instrumentation = provider.GetRequiredService<IEventStoreInstrumentation>();
        instrumentation.AppendObserver += list => observed.Add(list);

        var store = provider.GetRequiredService<IDocumentStore>();

        var streamId = Guid.NewGuid();
        await using var session = store.LightweightSession();
        session.Events.StartStream(streamId, new DiagObservedA("via di"));
        await session.SaveChangesAsync();

        observed.Count.ShouldBe(1);
        observed.Single().ShouldAllBe(e => e.StreamId == streamId);
    }
}

public class DiagDocForObservation
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
}
