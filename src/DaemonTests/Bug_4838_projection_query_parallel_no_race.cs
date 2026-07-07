using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Aggregation;
using Marten.Events.Daemon.Internals;
using Marten.Internal.Sessions;
using Marten.Services;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Storage;
using Xunit;

namespace DaemonTests;

/// <summary>
/// #4838 — Query&lt;T&gt;() / QueryAsync&lt;T&gt;(sql) / CreateBatchQuery() called from a
/// projection's Apply run on the async daemon's 10-wide parallel
/// <c>Block&lt;EventSliceExecution&gt;</c> workers against ONE shared
/// <see cref="ProjectionDocumentSession"/>. The version-capturing Lightweight
/// selectors did an unguarded get-or-add on <c>session.Versions</c> at construction
/// (VersionTracker._byType, a plain Dictionary) and per-row writes into the shared
/// inner Dictionary&lt;TId, long/Guid&gt; — corrupting the collections under the
/// parallel fan-out ("Operations that change non-concurrent collections must have
/// exclusive access"). #4667 Phase 3 closed LoadAsync/LoadManyAsync; this closes the
/// LINQ / raw-SQL / batch-query read entry points by handing the daemon session the
/// session-state-free Unversioned selector.
/// </summary>
public class Bug_4838_projection_query_parallel_no_race: BugIntegrationContext
{
    /// <summary>
    /// Deterministic detection of the racing surface: a query through the daemon's
    /// shared ProjectionDocumentSession must never write into the session-shared
    /// Versions dictionaries — for either the Numeric (revision) or Optimistic
    /// (Guid version) concurrency flavor, across LINQ, raw-SQL, and batch queries.
    /// A regular lightweight session is asserted as the contrast case so this test
    /// can't silently pass if version capture moves elsewhere.
    /// </summary>
    [Fact]
    public async Task queries_on_projection_session_do_not_touch_session_versions()
    {
        StoreOptions(opts =>
        {
            opts.DatabaseSchemaName = "bug4838_selectors";
            opts.Schema.For<Bug4838Question>().UseNumericRevisions(true);
            opts.Schema.For<Bug4838Block>().UseOptimisticConcurrency(true);
        });

        var projectId = Guid.NewGuid();
        var questionId = Guid.NewGuid();
        var blockId = Guid.NewGuid();

        await using (var setup = theStore.LightweightSession())
        {
            setup.Store(new Bug4838Question { Id = questionId, ProjectId = projectId });
            setup.Store(new Bug4838Block { Id = blockId, ProjectId = projectId });
            await setup.SaveChangesAsync();
        }

        // Contrast case: a plain lightweight session DOES capture versions from the
        // very same queries, proving the assertions below detect the shared-state write.
        await using (var regular = (DocumentSessionBase)theStore.LightweightSession())
        {
            await regular.Query<Bug4838Question>().Where(x => x.ProjectId == projectId).ToListAsync();
            await regular.Query<Bug4838Block>().Where(x => x.ProjectId == projectId).ToListAsync();

            regular.Versions.RevisionFor<Bug4838Question, Guid>(questionId).ShouldNotBeNull();
            regular.Versions.VersionFor<Bug4838Block, Guid>(blockId).ShouldNotBeNull();
        }

        // The daemon's shared session for a (range, tenant) — same construction as
        // ProjectionBatch.SessionForTenant / ISubscriptionRunner.ExecuteAsync.
        var batch = new ProjectionUpdateBatch(theStore.Options.Projections,
            (DocumentSessionBase)theSession, ShardExecutionMode.Continuous, CancellationToken.None);
        var sessionOptions = SessionOptions.ForDatabase(theStore.Options.Tenancy.Default.Database);
        await using (var projectionSession = new ProjectionDocumentSession(theStore, batch,
                         sessionOptions, ShardExecutionMode.Continuous))
        {
            // Every public read entry point named by #4838: LINQ...
            var questions = await projectionSession.Query<Bug4838Question>()
                .Where(x => x.ProjectId == projectId).ToListAsync();
            questions.Single().Id.ShouldBe(questionId);

            // ...raw SQL...
            var bySql = await projectionSession.QueryAsync<Bug4838Block>(
                "where data ->> 'ProjectId' = ?", projectId.ToString());
            bySql.Single().Id.ShouldBe(blockId);

            // ...and a batched query.
            var batchQuery = projectionSession.CreateBatchQuery();
            var batchedQuestions = batchQuery.Query<Bug4838Question>()
                .Where(x => x.ProjectId == projectId).ToList();
            var batchedBlocks = batchQuery.Query<Bug4838Block>()
                .Where(x => x.ProjectId == projectId).ToList();
            await batchQuery.Execute();
            (await batchedQuestions).Single().Id.ShouldBe(questionId);
            (await batchedBlocks).Single().Id.ShouldBe(blockId);

            // The whole point: nothing above may have written into the
            // session-shared Versions dictionaries the parallel slice workers race on.
            projectionSession.Versions.RevisionFor<Bug4838Question, Guid>(questionId).ShouldBeNull();
            projectionSession.Versions.VersionFor<Bug4838Block, Guid>(blockId).ShouldBeNull();

            // And the selectors handed to the daemon session are the
            // session-state-free Unversioned flavor for both concurrency modes.
            projectionSession.StorageFor<Bug4838Question>().BuildSelector(projectionSession)
                .ShouldBeOfType<FlatUnversionedClosedShapeLightweightSelector<Bug4838Question, Guid>>();
            projectionSession.StorageFor<Bug4838Block>().BuildSelector(projectionSession)
                .ShouldBeOfType<FlatUnversionedClosedShapeLightweightSelector<Bug4838Block, Guid>>();

            await batch.WaitForCompletion();
        }
    }

    /// <summary>
    /// End-to-end regression in the reporter's shape: an async aggregation whose
    /// Apply issues LINQ + raw-SQL + batched queries against registered side
    /// documents, rebuilt across enough streams to push the daemon's parallel
    /// Block(10, ...) fan-out through many concurrent Apply invocations sharing one
    /// session. Pre-fix this raced on VersionTracker's dictionaries
    /// (InvalidOperationException: "Operations that change non-concurrent
    /// collections..."); post-fix it must complete with correct aggregates.
    /// </summary>
    [Fact]
    public async Task queries_inside_apply_under_parallel_daemon_fanout()
    {
        StoreOptions(opts =>
        {
            opts.DatabaseSchemaName = "bug4838_daemon";
            opts.Projections.DaemonLockId = 48838;
            opts.Schema.For<Bug4838Question>().UseNumericRevisions(true);
            opts.Schema.For<Bug4838Block>().UseOptimisticConcurrency(true);
            opts.Projections.Add(new Bug4838TallyProjection(), ProjectionLifecycle.Async);
        });

        const int projectCount = 10;
        const int questionsPerProject = 3;
        const int blocksPerProject = 2;
        const int streamCount = 250;
        const int eventsPerStream = 4;

        var projectIds = Enumerable.Range(0, projectCount).Select(_ => Guid.NewGuid()).ToArray();
        await using (var session = theStore.LightweightSession())
        {
            foreach (var projectId in projectIds)
            {
                for (var i = 0; i < questionsPerProject; i++)
                {
                    session.Store(new Bug4838Question { Id = Guid.NewGuid(), ProjectId = projectId });
                }

                for (var i = 0; i < blocksPerProject; i++)
                {
                    session.Store(new Bug4838Block { Id = Guid.NewGuid(), ProjectId = projectId });
                }
            }

            await session.SaveChangesAsync();
        }

        var streamIds = new Guid[streamCount];
        await using (var session = theStore.LightweightSession())
        {
            for (var i = 0; i < streamCount; i++)
            {
                streamIds[i] = Guid.NewGuid();
                var projectId = projectIds[i % projectCount];
                session.Events.StartStream<Bug4838Tally>(
                    streamIds[i],
                    Enumerable.Range(0, eventsPerStream)
                        .Select(j => (object)new Bug4838Answered(projectId, j))
                        .ToArray());
            }

            await session.SaveChangesAsync();
        }

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.RebuildProjectionAsync<Bug4838Tally>(CancellationToken.None);

        await using var query = theStore.QuerySession();
        var aggregates = await query.LoadManyAsync<Bug4838Tally>(streamIds);

        aggregates.Count.ShouldBe(streamCount);
        foreach (var tally in aggregates)
        {
            tally.Applied.ShouldBe(eventsPerStream);
            tally.QuestionCount.ShouldBe(questionsPerProject);
            tally.BlockCount.ShouldBe(blocksPerProject);
        }
    }
}

public class Bug4838Tally
{
    public Guid Id { get; set; }
    public int Applied { get; set; }
    public int QuestionCount { get; set; }
    public int BlockCount { get; set; }
}

public class Bug4838Question
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
}

public class Bug4838Block
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
}

public record Bug4838Answered(Guid ProjectId, int Index);

public partial class Bug4838TallyProjection: SingleStreamProjection<Bug4838Tally, Guid>
{
    public async Task Apply(Bug4838Answered @event, Bug4838Tally tally, IQuerySession session)
    {
        // The session here is the daemon's shared ProjectionDocumentSession for
        // the (range, tenant); many slice workers run this Apply concurrently.
        // Exercise all three #4838 read entry points on every event.
        var questions = await session.Query<Bug4838Question>()
            .Where(x => x.ProjectId == @event.ProjectId)
            .ToListAsync();

        var blocksBySql = await session.QueryAsync<Bug4838Block>(
            "where data ->> 'ProjectId' = ?", @event.ProjectId.ToString());

        var batch = session.CreateBatchQuery();
        var batchedQuestions = batch.Query<Bug4838Question>()
            .Where(x => x.ProjectId == @event.ProjectId).ToList();
        var batchedBlocks = batch.Query<Bug4838Block>()
            .Where(x => x.ProjectId == @event.ProjectId).ToList();
        await batch.Execute();

        (await batchedQuestions).Count.ShouldBe(questions.Count);

        tally.QuestionCount = questions.Count;
        tally.BlockCount = Math.Max(blocksBySql.Count, (await batchedBlocks).Count);
        tally.Applied++;
    }
}
