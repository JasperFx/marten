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
    // evidence: in 33 green master runs (2026-09 to 2026-10-15) its first attempt failed in 13 —
    // "Task should throw System.AggregateException but did not" — and passed on a fresh-process
    // retry every time. It is still reported as FLAKY in the retry ledger and the job summary;
    // the trait keeps a known intermittent from turning the job red, it does not hide it. The
    // projection throws deterministically, so a CatchUpAsync that returns cleanly is worth an
    // investigation of its own (state left by an earlier test in the same process is the lead).
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
    [Fact]
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
