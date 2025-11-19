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

public class Bug_catching_up: OneOffConfigurationsContext
{
    [Fact]
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
        await theSession.SaveChangesAsync();

        using var daemon = await theStore.BuildProjectionDaemonAsync();

        var aggregated = await Should.ThrowAsync<AggregateException>(async () =>
        {
            await daemon.CatchUpAsync(CancellationToken.None);
        });

        aggregated.InnerExceptions[0].ShouldBeOfType<JasperFx.Events.Daemon.ApplyEventException>();
    }

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
        await theSession.SaveChangesAsync();

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        var aggregated = await Should.ThrowAsync<AggregateException>(async () =>
        {
            await daemon.CatchUpAsync(CancellationToken.None);
        });

        aggregated.InnerExceptions[0].ShouldBeOfType<MartenCommandException>();


        var failOnSave = await theSession.LoadAsync<FailsOnSave>(failOnSaveId);
        failOnSave.ShouldBeNull();
    }

}
