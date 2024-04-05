using System;
using System.Threading;
using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Storage;
using Marten.Testing.Harness;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace EventSourcingTests;

public class fetching_event_store_floor_by_time : OneOffConfigurationsContext
{
    [Fact]
    public async Task find_the_floor()
    {

        var start = new DateTimeOffset(2024, 1, 1, 0, 0, 0, 0.Seconds());
        var provider = new FakeTimeProvider(start);

        StoreOptions(opts =>
        {
            opts.Events.TimeProvider = provider;
        });

        theSession.Events.StartStream(Guid.NewGuid(), new AEvent(), new BEvent(), new CEvent(), new DEvent());
        theSession.Events.StartStream(Guid.NewGuid(), new AEvent(), new BEvent(), new CEvent(), new DEvent());
        theSession.Events.StartStream(Guid.NewGuid(), new AEvent(), new BEvent(), new CEvent(), new DEvent());
        theSession.Events.StartStream(Guid.NewGuid(), new AEvent(), new BEvent(), new CEvent(), new DEvent());

        await theSession.SaveChangesAsync();

        provider.Advance(5.Minutes());
        theSession.Events.StartStream(Guid.NewGuid(), new AEvent(), new BEvent(), new CEvent(), new DEvent());
        await theSession.SaveChangesAsync();

        provider.Advance(30.Seconds());
        theSession.Events.StartStream(Guid.NewGuid(), new AEvent(), new BEvent(), new CEvent(), new DEvent());
        await theSession.SaveChangesAsync();

        var position = await theStore.Storage.Database.As<MartenDatabase>()
            .FindEventStoreFloorAtTimeAsync(start.Add(3.Seconds()), CancellationToken.None);
        position.ShouldBe(17L);
    }
}
