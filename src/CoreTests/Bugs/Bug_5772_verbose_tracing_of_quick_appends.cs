using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace CoreTests.Bugs;

public record Bug5772Opened;

public record Bug5772Noted;

/// <summary>
/// #5772: with <c>TrackConnections = TrackLevel.Verbose</c>, <c>marten.append.event</c> was emitted only for an
/// operation deriving from <c>Weasel.Storage.AppendEventOperationBase</c>. The bulk Quick route (one
/// <c>mt_quick_append_events</c> call per stream, the V9 default's route for nearly every append) and the per-event
/// Quick route that reads its sequence back (a Quick-mode <c>StartStream</c>) are neither, and emitted nothing.
/// The trace should mean the same thing on every route: one activity event per appended event.
/// </summary>
public class Bug_5772_verbose_tracing_of_quick_appends: OneOffConfigurationsContext
{
    private void configure(EventAppendMode mode)
        => StoreOptions(opts =>
        {
            opts.Events.AppendMode = mode;
            opts.OpenTelemetry.TrackConnections = TrackLevel.Verbose;
            opts.DatabaseSchemaName = "bug_5772_" + mode.ToString().ToLowerInvariant();
        });

    private static async Task<string[]> appendedEventTypesTracedBy(Func<Task> act)
    {
        var traced = new ConcurrentQueue<string>();

        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Marten",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                foreach (var e in activity.Events.Where(x => x.Name == "marten.append.event"))
                {
                    var type = e.Tags.FirstOrDefault(x => x.Key == "Type").Value as string;
                    if (type != null && type.StartsWith("bug_5772_", StringComparison.Ordinal))
                    {
                        traced.Enqueue(type);
                    }
                }
            }
        };
        ActivitySource.AddActivityListener(listener);

        await act();

        return traced.ToArray();
    }

    [Theory]
    [InlineData(EventAppendMode.Rich)]
    [InlineData(EventAppendMode.Quick)]
    [InlineData(EventAppendMode.QuickWithServerTimestamps)]
    public async Task start_stream_traces_one_append_event_per_event(EventAppendMode mode)
    {
        configure(mode);
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        StreamAction? started = null;
        var traced = await appendedEventTypesTracedBy(async () =>
        {
            await using var session = theStore.LightweightSession();
            started = session.Events.StartStream(Guid.NewGuid(), new Bug5772Opened(), new Bug5772Noted());
            await session.SaveChangesAsync();
        });

        traced.ShouldBe(["bug_5772_opened", "bug_5772_noted"]);

        // The traced per-event route still reads each event's sequence back (#5454).
        started.ShouldNotBeNull().Events.ShouldAllBe(x => x.Sequence > 0);
    }

    [Theory]
    [InlineData(EventAppendMode.Rich)]
    [InlineData(EventAppendMode.Quick)]
    [InlineData(EventAppendMode.QuickWithServerTimestamps)]
    public async Task append_traces_one_append_event_per_event(EventAppendMode mode)
    {
        configure(mode);
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var id = Guid.NewGuid();
        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream(id, new Bug5772Opened());
            await session.SaveChangesAsync();
        }

        var traced = await appendedEventTypesTracedBy(async () =>
        {
            await using var session = theStore.LightweightSession();
            session.Events.Append(id, new Bug5772Noted(), new Bug5772Noted());
            await session.SaveChangesAsync();
        });

        traced.ShouldBe(["bug_5772_noted", "bug_5772_noted"]);
    }
}
