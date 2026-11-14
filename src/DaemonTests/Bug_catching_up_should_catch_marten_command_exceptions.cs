using System;
using System.Threading;
using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using JasperFx.Events.Projections;
using Marten.Exceptions;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DaemonTests.Bugs;

public class Bug_catching_up_apply_errors: OneOffConfigurationsContext
{
    // Retry is opt-in under the supervisor (JasperFx/bobcat#565), and this test earned it with
    // evidence: in 33 green master runs (2026-09 to 2026-10-15) its first attempt failed in 13 --
    // "Task should throw System.AggregateException but did not" -- and passed on a fresh-process
    // retry every time. It is reported as FLAKY in the retry ledger; the trait does not hide it.
    //
    // Root cause (marten#5696), found by reading JasperFx, not state left by an earlier test: the
    // schema is per class and dropped in StoreOptions(), so nothing carries over. CatchUpAsync
    // (JasperFxAsyncDaemon) collects failures through a Recorder subscribed to Database.Tracker,
    // and ShardStateTracker.PublishAsync records the state synchronously but delivers it to
    // listeners through an asynchronous Block<ShardState>. The apply failure is published by
    // SubscriptionAgent.ReportCriticalFailureAsync, then throwIfRecordedExceptions reads the
    // Recorder straight away. When the block's consumer has not yet delivered that state, the
    // Recorder is empty and CatchUpAsync returns cleanly. The fix belongs in JasperFx (drain the
    // tracker, or read the failure from the agent, before checking); the trait comes off once a
    // JasperFx release carrying it is adopted. Same cause as Bug_catching_up_command_errors below.
    [Fact]
    [Trait("Retry", "3")]
    public async Task should_catch_apply_event_exceptions()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Errors.SkipApplyErrors = false;
            opts.Projections.Add<SometimesFailingLetterCountsProjection>(ProjectionLifecycle.Async);
        });

        theSession.Events.StartStream<LetterCounts>(new AEvent(), new BEvent(), new CEvent(), new DEvent());
        theSession.Events.StartStream<LetterCounts>(new AEvent(), new BEvent(), new CEvent(), new DEvent());
        theSession.Events.StartStream<LetterCounts>(new AEvent(), new BEvent(), new CEvent(), new DEvent());
        theSession.Events.StartStream<LetterCounts>(new AEvent(), new ThrowError(false), new CEvent(), new DEvent());
        theSession.Events.StartStream<LetterCounts>(new AEvent(), new BEvent(), new CEvent(), new DEvent());
        theSession.Events.StartStream<LetterCounts>(new AEvent(), new BEvent(), new ThrowError(true), new DEvent());
        await theSession.SaveChangesAsync(TestContext.Current.CancellationToken);

        using var daemon = await theStore.BuildProjectionDaemonAsync();

        var aggregated = await Should.ThrowAsync<AggregateException>(async () =>
        {
            await daemon.CatchUpAsync(CancellationToken.None);
        });

        aggregated.InnerExceptions[0].ShouldBeOfType<JasperFx.Events.Daemon.ApplyEventException>();
    }
}

public class Bug_catching_up_command_errors: OneOffConfigurationsContext
{
    // The same intermittent, with the same root cause, as should_catch_apply_event_exceptions
    // above (marten#5696): CatchUpAsync checks its Recorder before the tracker's asynchronous
    // block has delivered the failure. It cost a whole job on PR 5780 (2026-10-28) without the
    // trait; a pass on a retry is still reported as FLAKY.
    [Fact]
    [Trait("Retry", "3")]
    public async Task should_catch_marten_command_exceptions()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Errors.SkipApplyErrors = false;
            opts.Projections.Add<FailsOnSaveProjection>(ProjectionLifecycle.Async);
            opts.Schema.For<FailsOnSave>()
                .Identity(x => x.Id)
                .Duplicate(x => x.ShouldNotBeNull, notNull: true);
        });

        var failOnSaveId = Guid.NewGuid();
        theSession.Events.StartStream<FailsOnSave>(new FailsOnSaveEventA());
        await theSession.SaveChangesAsync(TestContext.Current.CancellationToken);

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        var aggregated = await Should.ThrowAsync<AggregateException>(async () =>
        {
            await daemon.CatchUpAsync(CancellationToken.None);
        });

        aggregated.InnerExceptions[0].ShouldBeOfType<MartenCommandException>();


        var failOnSave = await theSession.LoadAsync<FailsOnSave>(failOnSaveId, TestContext.Current.CancellationToken);
        failOnSave.ShouldBeNull();
    }

}
