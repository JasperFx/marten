using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Events.Projections;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Bugs;

/// <summary>
/// https://github.com/JasperFx/jasperfx/issues/733.
///
/// A self-aggregating snapshot may declare its "Create" handler as an event-shaped constructor
/// (<c>public Foo(FooCreated e)</c>) instead of a named <c>static Create</c>. That works on its own,
/// but adding a <c>ShouldDelete</c> method to the same aggregate switched the source generator to a
/// different emitter — one that built its dispatch switch from named conventional methods only. The
/// constructor's event type got no case arm at all and no diagnostic, so the constructor never ran.
/// The next Apply-only event then built the aggregate through
/// <c>RuntimeHelpers.GetUninitializedObject</c>, skipping every field initializer: the reporter saw a
/// NullReferenceException out of an Apply that appended to a collection property, and in general got
/// a blank aggregate.
///
/// Fixed in JasperFx 2.61.0 by folding event constructors into the DetermineAction emitter's switch
/// (and into the generated EventTypes, which had the same omission on every self-aggregating path).
/// </summary>
public class Bug_jasperfx_733_event_constructor_create_with_should_delete : BugIntegrationContext
{
    [Fact]
    public async Task inline_constructor_create_runs_when_should_delete_is_present()
    {
        StoreOptions(opts => opts.Projections.Snapshot<TaggedThing>(SnapshotLifecycle.Inline));

        var streamId = Guid.NewGuid();

        theSession.Events.StartStream<TaggedThing>(streamId,
            new ThingCreated(streamId, "widget"),
            new TagAdded(streamId, "red"),
            new TagAdded(streamId, "large"));
        await theSession.SaveChangesAsync();

        var thing = await theSession.LoadAsync<TaggedThing>(streamId);

        thing.ShouldNotBeNull();

        // Before the fix this was null: the constructor never ran, so nothing assigned Name and the
        // Tags initializer never executed either.
        thing.Name.ShouldBe("widget");
        thing.Tags.ShouldBe(new List<string> { "red", "large" });
    }

    [Fact]
    public async Task async_constructor_create_runs_when_should_delete_is_present()
    {
        StoreOptions(opts => opts.Projections.Snapshot<TaggedThing>(SnapshotLifecycle.Async));

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();

        var streamId = Guid.NewGuid();

        theSession.Events.StartStream<TaggedThing>(streamId,
            new ThingCreated(streamId, "widget"),
            new TagAdded(streamId, "red"));
        await theSession.SaveChangesAsync();

        await daemon.WaitForNonStaleData(15.Seconds());

        var thing = await theSession.Query<TaggedThing>().FirstOrDefaultAsync(x => x.Id == streamId);

        thing.ShouldNotBeNull();
        thing.Name.ShouldBe("widget");
        thing.Tags.ShouldBe(new List<string> { "red" });
    }

    /// <summary>
    /// The ShouldDelete arm is the whole reason the broken emitter was selected, so assert it still
    /// deletes rather than only asserting the newly-restored Create.
    /// </summary>
    [Fact]
    public async Task should_delete_still_deletes_the_snapshot()
    {
        StoreOptions(opts => opts.Projections.Snapshot<TaggedThing>(SnapshotLifecycle.Inline));

        var streamId = Guid.NewGuid();

        theSession.Events.StartStream<TaggedThing>(streamId,
            new ThingCreated(streamId, "widget"),
            new TagAdded(streamId, "red"));
        await theSession.SaveChangesAsync();

        (await theSession.LoadAsync<TaggedThing>(streamId)).ShouldNotBeNull();

        theSession.Events.Append(streamId, new ThingDeleted(streamId));
        await theSession.SaveChangesAsync();

        (await theSession.LoadAsync<TaggedThing>(streamId)).ShouldBeNull();
    }

    /// <summary>
    /// Live aggregation goes through the same generated evolver, so a stream that opens with the
    /// constructor event has to fold identically without any snapshot on hand.
    /// </summary>
    [Fact]
    public async Task live_aggregation_uses_the_event_constructor()
    {
        StoreOptions(_ => { });

        var streamId = Guid.NewGuid();

        theSession.Events.StartStream<TaggedThing>(streamId,
            new ThingCreated(streamId, "widget"),
            new TagAdded(streamId, "red"));
        await theSession.SaveChangesAsync();

        var thing = await theSession.Events.AggregateStreamAsync<TaggedThing>(streamId);

        thing.ShouldNotBeNull();
        thing.Name.ShouldBe("widget");
        thing.Tags.ShouldBe(new List<string> { "red" });
    }
}

public record ThingCreated(Guid Id, string Name);

public record TagAdded(Guid Id, string Tag);

public record ThingDeleted(Guid Id);

public class TaggedThing
{
    // The Create handler is this constructor, NOT a named static Create — that distinction is the
    // whole bug. Tags is deliberately initialized here rather than in an Apply so that skipping the
    // constructor shows up as a null collection.
    public TaggedThing(ThingCreated @event)
    {
        Id = @event.Id;
        Name = @event.Name;
    }

    // Deliberately private: the generator only emits `new TaggedThing()` for a PUBLIC parameterless
    // constructor, and falls back to GetUninitializedObject otherwise. Keeping it private preserves
    // the exact reported symptom (an NRE out of Apply on a null Tags) instead of quietly papering
    // over it, while still giving System.Text.Json a constructor it can deserialize through.
    [JsonConstructor]
    private TaggedThing()
    {
    }

    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public List<string> Tags { get; set; } = new();

    public void Apply(TagAdded @event)
    {
        Tags.Add(@event.Tag);
    }

    public bool ShouldDelete(ThingDeleted @event) => true;
}
