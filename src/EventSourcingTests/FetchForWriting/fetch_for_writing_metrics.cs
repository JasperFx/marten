using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using JasperFx;
using JasperFx.Core.Reflection;
using JasperFx.Events.Projections;
using Marten.Services;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.FetchForWriting;

/// <summary>
/// Covers the opt-in marten.fetch_for_writing.events_replayed histogram: how many events a single
/// FetchForWriting() call had to read back and fold, tagged by aggregate type and fetch plan.
/// </summary>
public class fetch_for_writing_metrics: OneOffConfigurationsContext
{
    private const string MetricName = "marten.fetch_for_writing.events_replayed";

    [Fact]
    public async Task a_live_aggregate_replays_its_whole_stream_on_every_fetch()
    {
        StoreOptions(opts => opts.OpenTelemetry.TrackFetchForWritingMetrics());

        var streamId = Guid.NewGuid();
        theSession.Events.StartStream<MeteredAggregate>(streamId, new AEvent(), new AEvent(), new AEvent());
        await theSession.SaveChangesAsync();

        using var recorder = new EventsReplayedRecorder(MetricName);

        await using var session = theStore.LightweightSession();
        await session.Events.FetchForWriting<MeteredAggregate>(streamId);

        var measurement = recorder.Measurements.ShouldHaveSingleItem();
        measurement.Value.ShouldBe(3);
        measurement.FetchPlan.ShouldBe("Live");
        measurement.AggregateType.ShouldBe(typeof(MeteredAggregate).FullNameInCode());

        // ...and again on the very next fetch, which is the whole point of measuring this
        await using var second = theStore.LightweightSession();
        await second.Events.FetchForWriting<MeteredAggregate>(streamId);

        recorder.Measurements.Select(x => x.Value).ShouldBe([3, 3]);
    }

    [Fact]
    public async Task an_inline_aggregate_replays_nothing()
    {
        StoreOptions(opts =>
        {
            opts.OpenTelemetry.TrackFetchForWritingMetrics();
            opts.Projections.Snapshot<MeteredAggregate>(SnapshotLifecycle.Inline);
        });

        var streamId = Guid.NewGuid();
        theSession.Events.StartStream<MeteredAggregate>(streamId, new AEvent(), new AEvent(), new AEvent());
        await theSession.SaveChangesAsync();

        using var recorder = new EventsReplayedRecorder(MetricName);

        await using var session = theStore.LightweightSession();
        var stream = await session.Events.FetchForWriting<MeteredAggregate>(streamId);

        // The snapshot is current by construction, so the aggregate arrives fully built with
        // zero events read back
        stream.Aggregate.ShouldNotBeNull();
        stream.Aggregate.ACount.ShouldBe(3);

        var measurement = recorder.Measurements.ShouldHaveSingleItem();
        measurement.Value.ShouldBe(0);
        measurement.FetchPlan.ShouldBe("Inline");
    }

    [Fact]
    public async Task nothing_is_recorded_without_the_opt_in()
    {
        // Note the absence of TrackFetchForWritingMetrics()
        StoreOptions(_ => { });

        var streamId = Guid.NewGuid();
        theSession.Events.StartStream<MeteredAggregate>(streamId, new AEvent(), new AEvent());
        await theSession.SaveChangesAsync();

        using var recorder = new EventsReplayedRecorder(MetricName);

        await using var session = theStore.LightweightSession();
        await session.Events.FetchForWriting<MeteredAggregate>(streamId);

        recorder.Measurements.ShouldBeEmpty();
    }
}

public class MeteredAggregate: IRevisioned
{
    public Guid Id { get; set; }
    public int Version { get; set; }
    public int ACount { get; set; }

    public void Apply(AEvent _)
    {
        ACount++;
    }
}

/// <summary>
/// Captures every measurement published to the named histogram on Marten's meter. MeterListener.Start()
/// also replays instruments that were published before it started, so the store can be built first.
/// </summary>
internal sealed class EventsReplayedRecorder: IDisposable
{
    private readonly MeterListener _listener;

    public EventsReplayedRecorder(string instrumentName)
    {
        _listener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == "Marten" && instrument.Name == instrumentName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            }
        };

        _listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            // tags is a ReadOnlySpan, so it has to be read here rather than in a local function
            string? aggregateType = null;
            string? fetchPlan = null;
            foreach (var tag in tags)
            {
                if (tag.Key == OpenTelemetryOptions.AggregateTypeTag)
                {
                    aggregateType = tag.Value?.ToString();
                }
                else if (tag.Key == OpenTelemetryOptions.FetchPlanTag)
                {
                    fetchPlan = tag.Value?.ToString();
                }
            }

            lock (Measurements)
            {
                Measurements.Add(new Measurement(value, aggregateType, fetchPlan));
            }
        });

        _listener.Start();
    }

    public List<Measurement> Measurements { get; } = new();

    public void Dispose()
    {
        _listener.Dispose();
    }

    public record Measurement(long Value, string? AggregateType, string? FetchPlan);
}
