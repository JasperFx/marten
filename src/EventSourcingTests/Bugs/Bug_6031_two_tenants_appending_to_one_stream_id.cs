using System;
using System.Linq;
using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using JasperFx.Events;
using Marten;
using Marten.Storage;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Bugs;

public class Bug_6031_two_tenants_appending_to_one_stream_id: OneOffConfigurationsContext
{
    [Fact]
    public async Task append_by_guid_from_two_tenants_in_one_save_keeps_each_tenant_in_its_own_stream()
    {
        StoreOptions(opts => opts.Events.TenancyStyle = TenancyStyle.Conjoined, true);

        var id = Guid.NewGuid();
        theSession.ForTenant("red").Events.Append(id, new AEvent());
        theSession.ForTenant("blue").Events.Append(id, new BEvent());
        await theSession.SaveChangesAsync();

        await using var red = theStore.QuerySession("red");
        var redEvents = await red.Events.FetchStreamAsync(id);
        redEvents.Select(x => x.Data.GetType()).ShouldBe([typeof(AEvent)]);

        await using var blue = theStore.QuerySession("blue");
        var blueEvents = await blue.Events.FetchStreamAsync(id);
        blueEvents.Select(x => x.Data.GetType()).ShouldBe([typeof(BEvent)]);
    }

    [Fact]
    public async Task append_by_string_from_two_tenants_in_one_save_keeps_each_tenant_in_its_own_stream()
    {
        StoreOptions(opts =>
        {
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.StreamIdentity = StreamIdentity.AsString;
        }, true);

        var key = "s-" + Guid.NewGuid();
        theSession.ForTenant("red").Events.Append(key, new AEvent());
        theSession.ForTenant("blue").Events.Append(key, new BEvent());
        await theSession.SaveChangesAsync();

        await using var red = theStore.QuerySession("red");
        (await red.Events.FetchStreamAsync(key)).Select(x => x.Data.GetType()).ShouldBe([typeof(AEvent)]);

        await using var blue = theStore.QuerySession("blue");
        (await blue.Events.FetchStreamAsync(key)).Select(x => x.Data.GetType()).ShouldBe([typeof(BEvent)]);
    }
}
