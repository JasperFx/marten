using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Aggregation;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.FetchForWriting;

public class fetch_for_writing_and_projection_metadata_for_inline_projections : OneOffConfigurationsContext
{
    [Theory]
    [InlineData(ProjectionLifecycle.Inline, EventAppendMode.Quick)]
    [InlineData(ProjectionLifecycle.Inline, EventAppendMode.Rich)]
    public async Task can_use_version_metadata_on_new_stream(ProjectionLifecycle lifecycle, EventAppendMode mode)
    {
        StoreOptions(opts =>
        {
            opts.Projections.Add<ProjectionWithVersions>(lifecycle);
            opts.Events.AppendMode = mode;
            opts.Events.EnableSideEffectsOnInlineProjections = true;
        });

        ProjectionWithVersions.VersionsSeen.Clear();

        var streamId = Guid.NewGuid();

        var stream = await theSession.Events.FetchForWriting<VersionedGuy>(streamId);
        stream.AppendMany(new AEvent(), new BEvent(), new CEvent(), new DEvent());
        await theSession.SaveChangesAsync();

        ProjectionWithVersions.VersionsSeen.ShouldBe([1, 2, 3, 4]);

    }

    [Theory]
    [InlineData(ProjectionLifecycle.Inline, EventAppendMode.Quick)]
    [InlineData(ProjectionLifecycle.Inline, EventAppendMode.Rich)]
    public async Task can_use_version_metadata_on_start_stream(ProjectionLifecycle lifecycle, EventAppendMode mode)
    {
        StoreOptions(opts =>
        {
            opts.Projections.Add<ProjectionWithVersions>(lifecycle);
            opts.Events.AppendMode = mode;
            opts.Events.EnableSideEffectsOnInlineProjections = true;
        });

        ProjectionWithVersions.VersionsSeen.Clear();

        var streamId = Guid.NewGuid();

        var stream = theSession.Events.StartStream<VersionedGuy>(streamId, new AEvent(), new BEvent(), new CEvent(), new DEvent());

        await theSession.SaveChangesAsync();

        ProjectionWithVersions.VersionsSeen.ShouldBe([1, 2, 3, 4]);

    }

    [Theory]
    [InlineData(ProjectionLifecycle.Inline, EventAppendMode.Quick)]
    [InlineData(ProjectionLifecycle.Inline, EventAppendMode.Rich)]
    public async Task can_use_version_metadata_on_existing_stream(ProjectionLifecycle lifecycle, EventAppendMode mode)
    {
        StoreOptions(opts =>
        {
            opts.Projections.Add<ProjectionWithVersions>(lifecycle);
            opts.Events.AppendMode = mode;
            opts.Events.EnableSideEffectsOnInlineProjections = true;
        });

        ProjectionWithVersions.VersionsSeen.Clear();

        var streamId = Guid.NewGuid();

        theSession.Events.StartStream<VersionedGuy>(streamId, new AEvent(), new BEvent(), new CEvent(), new DEvent());
        await theSession.SaveChangesAsync();

        ProjectionWithVersions.VersionsSeen.Clear();

        var stream = await theSession.Events.FetchForWriting<VersionedGuy>(streamId);
        stream.AppendMany(new AEvent(), new BEvent(), new BEvent(), new DEvent());
        await theSession.SaveChangesAsync();

        ProjectionWithVersions.VersionsSeen.ShouldBe([5, 6, 7, 8]);

    }

    [Theory]
    [InlineData(ProjectionLifecycle.Inline, EventAppendMode.Quick)]
    [InlineData(ProjectionLifecycle.Inline, EventAppendMode.Rich)]
    public async Task can_use_version_metadata_on_existing_stream_with_expected_version(ProjectionLifecycle lifecycle, EventAppendMode mode)
    {
        StoreOptions(opts =>
        {
            opts.Projections.Add<ProjectionWithVersions>(lifecycle);
            opts.Events.AppendMode = mode;
            opts.Events.EnableSideEffectsOnInlineProjections = true;
        });

        ProjectionWithVersions.VersionsSeen.Clear();

        var streamId = Guid.NewGuid();

        theSession.Events.StartStream<VersionedGuy>(streamId, new AEvent(), new BEvent(), new CEvent(), new DEvent());
        await theSession.SaveChangesAsync();

        ProjectionWithVersions.VersionsSeen.Clear();

        var stream = await theSession.Events.FetchForWriting<VersionedGuy>(streamId, 4);
        stream.AppendMany(new AEvent(), new BEvent(), new BEvent(), new DEvent());
        await theSession.SaveChangesAsync();

        ProjectionWithVersions.VersionsSeen.ShouldBe([5, 6, 7, 8]);

    }

    // Regression for #4481: when an Evolve-based inline projection's stream is
    // fetched via FetchForWriting and no events are appended, an unrelated
    // document insert in the same session was failing with
    // JasperFx.ConcurrencyException because the empty stream still flowed
    // through the inline-projection Apply step and queued a storage operation
    // that did an optimistic-concurrency check against the unchanged version.
    [Fact]
    public async Task fetch_for_writing_without_appending_does_not_block_unrelated_inserts()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Add<ProjectionWithVersions>(ProjectionLifecycle.Inline);
            opts.Schema.For<UnrelatedDoc>().Identity(x => x.Id);
        });

        var streamId = Guid.NewGuid();

        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream<VersionedGuy>(streamId, new AEvent(), new BEvent());
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            // Fetch the stream but deliberately do not append any new events.
            await session.Events.FetchForWriting<VersionedGuy>(streamId);

            // Insert an unrelated document on the same session.
            session.Store(new UnrelatedDoc { Id = streamId, Note = "no new events" });

            // Should NOT throw — the empty stream must not propagate an
            // optimistic-concurrency check on the unchanged aggregate.
            await session.SaveChangesAsync();
        }

        await using var verify = theStore.QuerySession();
        var unrelated = await verify.LoadAsync<UnrelatedDoc>(streamId);
        unrelated.ShouldNotBeNull();
        unrelated.Note.ShouldBe("no new events");
    }
}

public class ProjectionWithVersions : SingleStreamProjection<VersionedGuy, Guid>
{
    public static List<long> VersionsSeen { get; } = new();

    public override ValueTask RaiseSideEffects(IDocumentOperations operations, IEventSlice<VersionedGuy> slice)
    {
        VersionsSeen.AddRange(slice.Events().Select(x => x.Version));

        return new ValueTask();
    }

    public override VersionedGuy Evolve(VersionedGuy snapshot, Guid id, IEvent e)
    {
        snapshot ??= new VersionedGuy { Id = id };
        switch (e.Data)
        {
            case AEvent:
                snapshot.ACount++;
                break;
            case BEvent:
                snapshot.ACount++;
                break;
            case CEvent:
                snapshot.ACount++;
                break;
            case DEvent:
                snapshot.ACount++;
                break;
        }

        return snapshot;
    }
}

public class VersionedGuy
{
    public Guid Id { get; set; }
    public int ACount { get; set; }
    public int BCount { get; set; }
    public int CCount { get; set; }
    public int DCount { get; set; }

    public int Version { get; set; }
}

public class UnrelatedDoc
{
    public Guid Id { get; set; }
    public string Note { get; set; } = "";
}
