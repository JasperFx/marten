using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Rewriting;

/// <summary>
///     jasperfx#1215: what only Marten can get wrong behind <c>IEventStore.RewriteStoredEventsAsync</c>. The seam's
///     contract is pinned by <c>EventRewriteCompliance</c>.
/// </summary>
public class rewriting_through_the_event_store_seam: OneOffConfigurationsContext
{
    public record ParcelLabelledV1(string Label);

    public record ParcelLabelled(string Text);

    public record ParcelWeighed(int Grams);

    private const string Shout = "shout";
    private const string FailOnTheThird = "fail-on-the-third";

    private static int _seen;

    private void configure() => StoreOptions(opts =>
    {
        opts.Events.Upcast<ParcelLabelledV1, ParcelLabelled>(x => new ParcelLabelled(x.Label));
        opts.Events.AddEventType<ParcelWeighed>();
        opts.EventGraph.AddRewriteTransform<ParcelLabelled>(Shout, x => x with { Text = x.Text + "!" });
        opts.EventGraph.AddRewriteTransform<ParcelWeighed>(FailOnTheThird, x =>
            Interlocked.Increment(ref _seen) == 3
                ? throw new InvalidOperationException("The third weighing")
                : x with { Grams = x.Grams + 1 });
    });

    private IEventDatabase Database => (IEventDatabase)theStore.Storage.Database;

    private IEventStore EventStore => theStore;

    private static EventRewriteDefinition aRewrite() => new()
    {
        Name = "seam" + Guid.NewGuid().ToString("N")[..12]
    };

    private async Task<Guid> appendAsync(params object[] events)
    {
        var id = Guid.NewGuid();
        await using var session = theStore.LightweightSession();
        session.Events.StartStream(id, events);
        await session.SaveChangesAsync();
        return id;
    }

    private async Task<IReadOnlyList<IEvent>> streamAsync(Guid id)
    {
        await using var session = theStore.QuerySession();
        return await session.Events.FetchStreamAsync(id);
    }

    private async Task<IReadOnlyList<EventRewriteProgress>> rewriteAsync(EventRewriteDefinition definition)
    {
        var run = await EventRewriteRun.FromMarksAsync(Database, definition, CancellationToken.None);
        return await EventStore.RewriteStoredEventsAsync(run, CancellationToken.None);
    }

    [Fact]
    public async Task a_definition_that_names_an_old_alias_selects_every_name_its_type_is_stored_under()
    {
        configure();
        var old = await appendAsync(new ParcelLabelledV1("old"));
        var current = await appendAsync(new ParcelLabelled("new"));

        // The upcaster's old name is the only one listed, and the type it reads into is stored under another
        var definition = aRewrite();
        definition.EventTypes = ["parcel_labelled_v1"];

        var total = (await rewriteAsync(definition)).ShouldHaveSingleItem();

        // Both are read, and only the one under the old name is stored differently from how it would be today
        total.Visited.ShouldBe(2);
        total.Rewritten.ShouldBe(1);
        (await streamAsync(old)).Single().Data.ShouldBe(new ParcelLabelled("old"));
        (await streamAsync(current)).Single().Data.ShouldBe(new ParcelLabelled("new"));
    }

    [Fact]
    public async Task a_stored_name_no_type_answers_to_is_read_and_left_alone()
    {
        configure();
        await appendAsync(new ParcelWeighed(10));

        await using (var conn = theStore.Storage.Database.CreateConnection())
        {
            await conn.OpenAsync();
            await new NpgsqlCommand($"update {SchemaName}.mt_events set type = 'parcel_retired', mt_dotnet_type = 'Retired.ParcelRetired, Retired'", conn)
                .ExecuteNonQueryAsync();
        }

        var definition = aRewrite();
        definition.EventTypes = ["parcel_retired"];

        var total = (await rewriteAsync(definition)).ShouldHaveSingleItem();

        total.Visited.ShouldBe(1);
        total.UnknownTypes.ShouldBe(1);
        total.Rewritten.ShouldBe(0);
        total.Completed.ShouldBeTrue();
    }

    [Fact]
    public async Task a_page_that_fails_leaves_its_events_and_its_cursor_as_they_were()
    {
        configure();
        _seen = 0;
        var id = await appendAsync(new ParcelWeighed(10), new ParcelWeighed(20), new ParcelWeighed(30),
            new ParcelWeighed(40));

        var definition = aRewrite();
        definition.Transforms = [FailOnTheThird];
        definition.BatchSize = 2;

        // The second page throws after the transform ran on its first event
        await Should.ThrowAsync<InvalidOperationException>(() => rewriteAsync(definition));

        var cursorKey = EventRangeMarks.CursorKey(EventRangeMarks.RewriteKey(definition.Name));
        var events = await streamAsync(id);
        (await Database.FetchEventRangeMarkAsync(cursorKey, null, CancellationToken.None))
            .ShouldBe(events[1].Sequence);
        events.Select(x => ((ParcelWeighed)x.Data).Grams).ShouldBe([11, 21, 30, 40]);

        // Resumed after the stored cursor, every event went through the transform once
        await rewriteAsync(definition);

        (await streamAsync(id)).Select(x => ((ParcelWeighed)x.Data).Grams).ShouldBe([11, 21, 31, 41]);
    }

    [Fact]
    public async Task the_stop_after_option_leaves_later_events_alone()
    {
        configure();
        var first = await appendAsync(new ParcelLabelled("first"));
        var ceiling = await theStore.Storage.Database.FetchHighestEventSequenceNumber();
        var second = await appendAsync(new ParcelLabelled("second"));

        var result = await theStore.Advanced.RewriteStoredEventsAsync(x =>
        {
            x.Transform<ParcelLabelled>(e => e with { Text = e.Text.ToUpperInvariant() });
            x.StopAfter(ceiling);
        });

        result.Databases.Single().Rewritten.ShouldBe(1);
        (await streamAsync(first)).Single().Data.ShouldBe(new ParcelLabelled("FIRST"));
        (await streamAsync(second)).Single().Data.ShouldBe(new ParcelLabelled("second"));
    }

    [Fact]
    public async Task a_run_for_another_store_is_refused()
    {
        configure();
        using var other = SeparateStore(opts => opts.DatabaseSchemaName = SchemaName + "_other");

        var run = await EventRewriteRun.FromMarksAsync((IEventDatabase)other.Storage.Database, aRewrite(),
            CancellationToken.None);

        await Should.ThrowAsync<ArgumentOutOfRangeException>(() =>
            EventStore.RewriteStoredEventsAsync(run, CancellationToken.None));
    }
}
