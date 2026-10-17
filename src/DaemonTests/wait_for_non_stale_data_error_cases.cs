using System;
using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Exceptions;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;
using ApplyEventException = JasperFx.Events.Daemon.ApplyEventException;

namespace DaemonTests;

public class wait_for_non_stale_data_error_cases : OneOffConfigurationsContext
{
    // WaitForNonStaleProjectionDataAsync documents a TimeoutException. A timeout that lands inside its first
    // round trip, here a cold store's first storage check, used to escape as a TaskCanceledException, an
    // OperationCanceledException or an NpgsqlException wrapping one, depending on where it caught the call.
    // Bobcat's catch-up diagnosis only recognises a TimeoutException, so on a slow CI runner the forced timeout in
    // catch_up_timeout_diagnostics surfaced as an undiagnosed TaskCanceledException (bobcat#562).
    [Theory]
    [InlineData(1)]
    [InlineData(20)]
    [InlineData(100)]
    public async Task a_timeout_during_the_first_round_trip_is_still_a_timeout_exception(int milliseconds)
    {
        StoreOptions(opts =>
        {
            opts.DatabaseSchemaName = $"wait_cold_{milliseconds}";
            opts.Projections.Add<SometimesFailingLetterCountsProjection>(ProjectionLifecycle.Async);
        });

        await Should.ThrowAsync<TimeoutException>(() =>
            theStore.Storage.Database.WaitForNonStaleProjectionDataAsync(TimeSpan.FromMilliseconds(milliseconds)));
    }

    [Fact]
    public async Task get_a_good_timeout_exception()
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

        await daemon.StartAllAsync();

        var aggregated = await Should.ThrowAsync<AggregateException>(async () =>
        {
            await daemon.WaitForNonStaleData(5.Seconds());
        });

        aggregated.InnerExceptions[0].ShouldBeOfType<TimeoutException>();
        aggregated.InnerExceptions[1].ShouldBeOfType<ApplyEventException>();
    }

    [Fact]
    public async Task get_a_good_command_exception()
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

        await daemon.StartAllAsync();

        var aggregated = await Should.ThrowAsync<AggregateException>(async () =>
        {
            await daemon.WaitForNonStaleData(5.Seconds());
        });

        aggregated.InnerExceptions[0].ShouldBeOfType<TimeoutException>();
        aggregated.InnerExceptions[1].ShouldBeOfType<MartenCommandException>();


        var failOnSave = await theSession.LoadAsync<FailsOnSave>(failOnSaveId, TestContext.Current.CancellationToken);
        failOnSave.ShouldBeNull();
    }
}

public record ThrowError(bool ShouldThrow);

public partial class SometimesFailingLetterCountsProjection: SingleStreamProjection<LetterCounts, Guid>
{
    public override LetterCounts Evolve(LetterCounts snapshot, Guid id, IEvent e)
    {
        snapshot ??= new() { Id = id };
        switch (e.Data)
        {
            case AEvent _:
                snapshot.ACount++;
                break;
            case BEvent _:
                snapshot.BCount++;
                break;
            case CEvent _:
                snapshot.CCount++;
                break;
            case DEvent _:
                snapshot.DCount++;
                break;
            case ThrowError x:
                if (x.ShouldThrow) throw new("You stink!");
                break;
        }

        return snapshot;
    }
}

public record FailsOnSave(Guid Id, string ShouldNotBeNull);
public record FailsOnSaveEventA;

public partial class FailsOnSaveProjection: SingleStreamProjection<FailsOnSave, Guid>
{
    public static FailsOnSave Create(IEvent<FailsOnSaveEventA> @event) => new(@event.StreamId, null!);
}
