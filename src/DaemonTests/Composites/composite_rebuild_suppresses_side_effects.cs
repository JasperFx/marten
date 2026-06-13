using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DaemonTests.TestingSupport;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Aggregation;
using Marten.Internal.Sessions;
using Marten.Services;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace DaemonTests.Composites;

// marten#4729 regression: a composite projection whose stage member raises side effects
// (RaiseSideEffects -> slice.PublishMessage / slice.AppendEvent) must fire those side effects
// during continuous/live operation but NOT during a rebuild — matching the classic single-stream
// rebuild semantics pinned by side_effects_in_aggregations.side_effects_do_not_happen_in_rebuilds.
//
// The bug: CompositeReplayExecutor set ShardExecutionMode only on the parent composite execution,
// while the member executions defaulted to ShardExecutionMode.Continuous and were never updated,
// so the optimized composite rebuild re-published the member's side-effect messages every time.
// Fixed in JasperFx 2.9.12 (jasperfx#447): CompositeExecution propagates its Mode to every member.
public class composite_rebuild_suppresses_side_effects: DaemonContext
{
    public composite_rebuild_suppresses_side_effects(ITestOutputHelper output): base(output)
    {
    }

    [Fact]
    public async Task side_effects_fire_continuously_but_not_during_a_composite_rebuild()
    {
        var outbox = new RecordingMessageOutbox();

        StoreOptions(opts =>
        {
            opts.Events.MessageOutbox = outbox;

            opts.Projections.CompositeProjectionFor("SideEffectComposite", c =>
            {
                c.Add<CsCounterProjection>(stageNumber: 1);
                c.Add<CsNoticeProjection>(stageNumber: 2);
            });
        }, true);

        await theStore.Advanced.Clean.DeleteAllDocumentsAsync();
        await theStore.Advanced.Clean.DeleteAllEventDataAsync();

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();

        var streamIds = new List<Guid>();
        for (var i = 0; i < 10; i++)
        {
            var id = Guid.NewGuid();
            streamIds.Add(id);
            theSession.Events.StartStream<CsCounter>(id, new CsLeg(), new CsLeg(), new CsLeg());
        }

        await theSession.SaveChangesAsync();

        await daemon.WaitForNonStaleData(30.Seconds());

        // Both stages materialized
        (await theSession.Query<CsCounter>().CountAsync()).ShouldBe(10);
        (await theSession.Query<CsNotice>().CountAsync()).ShouldBe(10);

        // The stage-2 member published a CsNoticed message per stream during continuous operation
        var publishedDuringContinuous = outbox.PublishedMessages.OfType<CsNoticed>().Count();
        publishedDuringContinuous.ShouldBe(10);

        await daemon.StopAllAsync();

        // Capture the baseline, then rebuild the composite by its registered name
        var messageCountBeforeRebuild = outbox.PublishedMessages.Count;

        await daemon.RebuildProjectionAsync("SideEffectComposite", 60.Seconds(), CancellationToken.None);

        // The rebuild re-derived the read models...
        (await theSession.Query<CsCounter>().CountAsync()).ShouldBe(10);
        (await theSession.Query<CsNotice>().CountAsync()).ShouldBe(10);

        // ...but published NO additional side-effect messages. Before the #4729 fix the composite
        // members ran in Continuous mode during the rebuild and re-published all 10 CsNoticed messages.
        outbox.PublishedMessages.Count.ShouldBe(messageCountBeforeRebuild);
        outbox.PublishedMessages.OfType<CsNoticed>().Count().ShouldBe(publishedDuringContinuous);
    }
}

public record CsLeg;

public record CsNoticed(Guid Id);

public class CsCounter
{
    public Guid Id { get; set; }
    public int Legs { get; set; }
    public int Version { get; set; }
}

public partial class CsCounterProjection: SingleStreamProjection<CsCounter, Guid>
{
    public CsCounterProjection() => Name = "CsCounter";

    public void Apply(CsCounter agg, CsLeg _) => agg.Legs++;
}

public class CsNotice
{
    public Guid Id { get; set; }
    public int Legs { get; set; }
    public int Version { get; set; }
}

public partial class CsNoticeProjection: SingleStreamProjection<CsNotice, Guid>
{
    public CsNoticeProjection() => Name = "CsNotice";

    public void Apply(CsNotice agg, CsLeg _) => agg.Legs++;

    public override ValueTask RaiseSideEffects(IDocumentOperations operations, IEventSlice<CsNotice> slice)
    {
        slice.PublishMessage(new CsNoticed(slice.Events().First().StreamId));
        return new ValueTask();
    }
}

public class RecordingMessageOutbox: IMessageOutbox
{
    private readonly List<RecordingMessageBatch> _batches = new();

    public IReadOnlyList<object> PublishedMessages =>
        _batches.SelectMany(x => x.Messages).Select(x => x.message).ToList();

    public ValueTask<IMessageBatch> CreateBatch(DocumentSessionBase session)
    {
        var batch = new RecordingMessageBatch();
        lock (_batches)
        {
            _batches.Add(batch);
        }

        return new ValueTask<IMessageBatch>(batch);
    }
}

public record OutboxMessage(string tenantId, object message);

public class RecordingMessageBatch: IMessageBatch
{
    private readonly List<OutboxMessage> _messages = new();

    public IReadOnlyList<OutboxMessage> Messages
    {
        get
        {
            lock (_messages)
            {
                return _messages.ToList();
            }
        }
    }

    public Task AfterCommitAsync(IDocumentSession session, IChangeSet commit, CancellationToken token) =>
        Task.CompletedTask;

    public Task BeforeCommitAsync(IDocumentSession session, IChangeSet commit, CancellationToken token) =>
        Task.CompletedTask;

    public ValueTask PublishAsync<T>(T message, string tenantId)
    {
        lock (_messages)
        {
            _messages.Add(new OutboxMessage(tenantId, message));
        }

        return new ValueTask();
    }
}
