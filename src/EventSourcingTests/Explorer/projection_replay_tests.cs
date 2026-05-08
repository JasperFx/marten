#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Descriptors;
using JasperFx.Events;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Explorer;

public record CounterIncremented(int By);
public record CounterReset();
public record CounterErrors();

public class Counter
{
    public Guid Id { get; set; }
    public int Value { get; set; }

    public void Apply(CounterIncremented e) => Value += e.By;
    public void Apply(CounterReset _) => Value = 0;
    public void Apply(CounterErrors _) => throw new InvalidOperationException("apply blew up");
}

[Collection("OneOffs")]
public class projection_replay_tests: OneOffConfigurationsContext
{
    private void ConfigureStore()
    {
        StoreOptions(opts =>
        {
            opts.Events.AddEventType<CounterIncremented>();
            opts.Events.AddEventType<CounterReset>();
            opts.Events.AddEventType<CounterErrors>();

            opts.Projections.LiveStreamAggregation<Counter>();
        });
    }

    private static async Task<List<EventRecord>> ReadStreamRecordsAsync(IEventStore explorer, Guid streamId)
    {
        var list = new List<EventRecord>();
        await foreach (var e in explorer.ReadStreamAsync(streamId.ToString(), CancellationToken.None))
        {
            list.Add(e);
        }
        return list;
    }

    [Fact]
    public async Task run_projection_typed_replays_events_with_per_step_state()
    {
        ConfigureStore();
        var streamId = Guid.NewGuid();
        theSession.Events.Append(streamId,
            new CounterIncremented(1),
            new CounterIncremented(2),
            new CounterIncremented(3));
        await theSession.SaveChangesAsync();

        var explorer = (IEventStore)theStore;
        var records = await ReadStreamRecordsAsync(explorer, streamId);

        var timeline = await explorer.RunProjectionAsync<Counter>(
            projectionName: nameof(Counter),
            identity: streamId,
            events: records,
            startingState: null,
            ct: CancellationToken.None);

        timeline.Steps.Count.ShouldBe(3);
        timeline.Steps[0].After!.Value.ShouldBe(1);
        timeline.Steps[1].After!.Value.ShouldBe(3);
        timeline.Steps[2].After!.Value.ShouldBe(6);
        timeline.FinalState!.Value.ShouldBe(6);
        timeline.Steps.ShouldAllBe(s => s.Error == null);
    }

    [Fact]
    public async Task run_projection_typed_continues_past_throwing_event()
    {
        ConfigureStore();
        var streamId = Guid.NewGuid();
        theSession.Events.Append(streamId,
            new CounterIncremented(5),
            new CounterErrors(),
            new CounterIncremented(2));
        await theSession.SaveChangesAsync();

        var explorer = (IEventStore)theStore;
        var records = await ReadStreamRecordsAsync(explorer, streamId);

        var timeline = await explorer.RunProjectionAsync<Counter>(
            nameof(Counter), streamId, records, startingState: null, CancellationToken.None);

        timeline.Steps.Count.ShouldBe(3);
        timeline.Steps[0].Error.ShouldBeNull();
        timeline.Steps[0].After!.Value.ShouldBe(5);

        timeline.Steps[1].Error.ShouldNotBeNull();
        // After should equal Before when the apply threw — per ProjectionStepResult docs.
        timeline.Steps[1].After!.Value.ShouldBe(timeline.Steps[1].Before!.Value);

        timeline.Steps[2].Error.ShouldBeNull();
        // The third event applies on top of the last successful state.
        timeline.Steps[2].After!.Value.ShouldBe(7);
    }

    [Fact]
    public async Task run_projection_typed_uses_starting_state_for_pagination()
    {
        ConfigureStore();
        var streamId = Guid.NewGuid();
        theSession.Events.Append(streamId,
            new CounterIncremented(1),
            new CounterIncremented(2),
            new CounterIncremented(3),
            new CounterIncremented(4));
        await theSession.SaveChangesAsync();

        var explorer = (IEventStore)theStore;
        var records = await ReadStreamRecordsAsync(explorer, streamId);
        records.Count.ShouldBe(4);

        var firstHalf = records.Take(2).ToList();
        firstHalf.Count.ShouldBe(2);
        var secondHalf = records.Skip(2).ToList();
        secondHalf.Count.ShouldBe(2);

        var page1 = await explorer.RunProjectionAsync<Counter>(
            nameof(Counter), streamId, firstHalf, startingState: null, CancellationToken.None);

        page1.Steps.Count.ShouldBe(2);
        page1.FinalState!.Value.ShouldBe(3);

        var page2 = await explorer.RunProjectionAsync<Counter>(
            nameof(Counter), streamId, secondHalf, startingState: page1.FinalState, CancellationToken.None);

        page2.FinalState!.Value.ShouldBe(10);
        page2.Steps[0].Before!.Value.ShouldBe(3);
    }

    [Fact]
    public async Task run_projection_typed_throws_on_aggregate_type_mismatch()
    {
        ConfigureStore();
        var explorer = (IEventStore)theStore;

        await Should.ThrowAsync<ArgumentException>(async () =>
        {
            await explorer.RunProjectionAsync<string>(
                nameof(Counter), Guid.NewGuid(), Array.Empty<EventRecord>(), startingState: null, CancellationToken.None);
        });
    }

    [Fact]
    public async Task run_projection_by_name_returns_json_state_per_step()
    {
        ConfigureStore();
        var streamId = Guid.NewGuid();
        theSession.Events.Append(streamId,
            new CounterIncremented(10),
            new CounterIncremented(20));
        await theSession.SaveChangesAsync();

        var explorer = (IEventStore)theStore;
        var records = await ReadStreamRecordsAsync(explorer, streamId);

        var timeline = await explorer.RunProjectionByNameAsync(
            nameof(Counter), streamId, records, startingState: null, CancellationToken.None);

        timeline.Steps.Count.ShouldBe(2);
        timeline.Steps[0].After!.Value.GetProperty("Value").GetInt32().ShouldBe(10);
        timeline.Steps[1].After!.Value.GetProperty("Value").GetInt32().ShouldBe(30);
        timeline.FinalState!.Value.GetProperty("Value").GetInt32().ShouldBe(30);
    }

    [Fact]
    public async Task run_projection_by_name_records_error_message_on_failure()
    {
        ConfigureStore();
        var streamId = Guid.NewGuid();
        theSession.Events.Append(streamId,
            new CounterIncremented(1),
            new CounterErrors());
        await theSession.SaveChangesAsync();

        var explorer = (IEventStore)theStore;
        var records = await ReadStreamRecordsAsync(explorer, streamId);

        var timeline = await explorer.RunProjectionByNameAsync(
            nameof(Counter), streamId, records, startingState: null, CancellationToken.None);

        timeline.Steps[0].Error.ShouldBeNull();
        timeline.Steps[1].Error.ShouldNotBeNull();
        timeline.Steps[1].Error!.ShouldContain("apply blew up");
    }

    [Fact]
    public async Task run_projection_throws_for_unknown_projection_name()
    {
        ConfigureStore();
        var explorer = (IEventStore)theStore;

        await Should.ThrowAsync<ArgumentOutOfRangeException>(async () =>
        {
            await explorer.RunProjectionByNameAsync(
                "DoesNotExist", Guid.NewGuid(), Array.Empty<EventRecord>(), startingState: null, CancellationToken.None);
        });
    }
}
