using System;
using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten.Events.Aggregation;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DaemonTests;

public class wait_for_non_stale_data_error_cases : OneOffConfigurationsContext
{
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
        await theSession.SaveChangesAsync();

        using var daemon = await theStore.BuildProjectionDaemonAsync();

        await daemon.StartAllAsync();

        var aggregated = await Should.ThrowAsync<AggregateException>(async () =>
        {
            await daemon.WaitForNonStaleData(5.Seconds());
        });

        aggregated.InnerExceptions[0].ShouldBeOfType<TimeoutException>();
        aggregated.InnerExceptions[1].ShouldBeOfType<ApplyEventException>();


    }
}

public record ThrowError(bool ShouldThrow);

public class SometimesFailingLetterCountsProjection: SingleStreamProjection<LetterCounts, Guid>
{
    public override LetterCounts Evolve(LetterCounts snapshot, Guid id, IEvent e)
    {
        snapshot ??= new LetterCounts { Id = id };
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
                if (x.ShouldThrow) throw new Exception("You stink!");
                break;
        }

        return snapshot;
    }
}
