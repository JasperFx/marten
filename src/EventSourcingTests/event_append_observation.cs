using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Core.Reflection;
using JasperFx.Events;
using Marten;
using Marten.Events;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace EventSourcingTests;

public record DiagObservedA(string Note);

public record DiagObservedB(int Amount);

public class DiagDocForObservation
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
}

public interface IObservationStore: IDocumentStore;

/// <summary>
/// #4782: when <c>JasperFxOptions.EnableAdvancedTracking</c> is on, Marten auto-applies an
/// <see cref="Marten.IDocumentSessionListener"/> that forwards each committed unit of work's appended
/// events to the storage-agnostic <see cref="IEventStoreInstrumentation.AppendObserver"/>, so lifecycle
/// tooling (CritterWatch) can record runtime-observed "appends" edges. Covers main and ancillary stores.
/// </summary>
public class event_append_observation
{
    [Fact]
    public async Task observer_receives_the_appended_events_with_metadata()
    {
        using var host = await startHost("eao_basic", advancedTracking: true);
        var store = host.Services.GetRequiredService<IDocumentStore>().As<DocumentStore>();

        var observed = new List<IReadOnlyList<IEvent>>();
        store.Options.EventGraph.AppendObserver = list => observed.Add(list);

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
    public async Task no_listener_and_no_observation_when_advanced_tracking_is_off()
    {
        using var host = await startHost("eao_off", advancedTracking: false);
        var store = host.Services.GetRequiredService<IDocumentStore>().As<DocumentStore>();

        store.Options.Listeners.OfType<AppendEventObservationListener>().ShouldBeEmpty();

        var observed = new List<IReadOnlyList<IEvent>>();
        store.Options.EventGraph.AppendObserver = list => observed.Add(list);

        await using var session = store.LightweightSession();
        session.Events.StartStream(Guid.NewGuid(), new DiagObservedA("ignored"));
        await session.SaveChangesAsync();

        observed.ShouldBeEmpty();
    }

    [Fact]
    public async Task observer_carries_the_stream_key_for_string_identified_streams()
    {
        using var host = await startHost("eao_string", advancedTracking: true,
            opts => opts.Events.StreamIdentity = StreamIdentity.AsString);
        var store = host.Services.GetRequiredService<IDocumentStore>().As<DocumentStore>();

        var observed = new List<IReadOnlyList<IEvent>>();
        store.Options.EventGraph.AppendObserver = list => observed.Add(list);

        await using var session = store.LightweightSession();
        session.Events.StartStream("metrics/widget-7", new DiagObservedA("k"));
        await session.SaveChangesAsync();

        observed.Single().ShouldAllBe(e => e.StreamKey == "metrics/widget-7");
    }

    [Fact]
    public async Task observer_carries_the_tenant_id_when_multi_tenanted()
    {
        using var host = await startHost("eao_tenant", advancedTracking: true,
            opts => opts.Events.TenancyStyle = TenancyStyle.Conjoined);
        var store = host.Services.GetRequiredService<IDocumentStore>().As<DocumentStore>();

        var observed = new List<IReadOnlyList<IEvent>>();
        store.Options.EventGraph.AppendObserver = list => observed.Add(list);

        await using var session = store.LightweightSession("acme");
        session.Events.StartStream(Guid.NewGuid(), new DiagObservedA("t"));
        await session.SaveChangesAsync();

        observed.Single().ShouldAllBe(e => e.TenantId == "acme");
    }

    [Fact]
    public async Task observer_does_not_fire_when_no_events_are_appended()
    {
        using var host = await startHost("eao_doconly", advancedTracking: true);
        var store = host.Services.GetRequiredService<IDocumentStore>().As<DocumentStore>();

        var fired = false;
        store.Options.EventGraph.AppendObserver = _ => fired = true;

        await using var session = store.LightweightSession();
        session.Store(new DiagDocForObservation { Id = Guid.NewGuid(), Name = "doc-only" });
        await session.SaveChangesAsync();

        fired.ShouldBeFalse();
    }

    [Fact]
    public async Task a_throwing_observer_does_not_break_save_changes()
    {
        using var host = await startHost("eao_throw", advancedTracking: true);
        var store = host.Services.GetRequiredService<IDocumentStore>().As<DocumentStore>();

        store.Options.EventGraph.AppendObserver = _ => throw new InvalidOperationException("boom");

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
    public async Task observer_set_through_the_di_instrumentation_bridge_fires()
    {
        await dropSchema("eao_bridge");

        var services = new ServiceCollection();
        services.AddJasperFx(o => o.EnableAdvancedTracking = true);
        services.AddMarten(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "eao_bridge";
        });

        await using var provider = services.BuildServiceProvider();

        // A consumer (e.g. a CritterWatch satellite) sets the observer on the storage-agnostic
        // instrumentation surface before the store is built.
        var observed = new List<IReadOnlyList<IEvent>>();
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

    [Fact]
    public async Task listener_is_applied_to_ancillary_stores()
    {
        await dropSchema("eao_anc_main");
        await dropSchema("eao_anc_first");

        using var host = await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddJasperFx(o => o.EnableAdvancedTracking = true);

                services.AddMarten(m =>
                {
                    m.Connection(ConnectionSource.ConnectionString);
                    m.DatabaseSchemaName = "eao_anc_main";
                });

                services.AddMartenStore<IObservationStore>(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DatabaseSchemaName = "eao_anc_first";
                });
            }).StartAsync();

        var ancillary = host.Services.GetRequiredService<IObservationStore>().As<DocumentStore>();
        ancillary.Options.Listeners.OfType<AppendEventObservationListener>().ShouldNotBeEmpty();

        var observed = new List<IReadOnlyList<IEvent>>();
        ancillary.Options.EventGraph.AppendObserver = list => observed.Add(list);

        var streamId = Guid.NewGuid();
        await using var session = ancillary.LightweightSession();
        session.Events.StartStream(streamId, new DiagObservedA("ancillary"));
        await session.SaveChangesAsync();

        observed.Count.ShouldBe(1);
        observed.Single().ShouldAllBe(e => e.StreamId == streamId);
    }

    private static async Task<IHost> startHost(string schema, bool advancedTracking,
        Action<StoreOptions>? configure = null)
    {
        await dropSchema(schema);

        return await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                if (advancedTracking)
                {
                    services.AddJasperFx(o => o.EnableAdvancedTracking = true);
                }

                services.AddMarten(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DatabaseSchemaName = schema;
                    configure?.Invoke(opts);
                });
            }).StartAsync();
    }

    private static async Task dropSchema(string schema)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await conn.DropSchemaAsync(schema);
    }
}
