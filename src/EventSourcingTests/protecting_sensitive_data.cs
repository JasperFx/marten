using System;
using System.Linq;
using System.Threading.Tasks;
using Marten.Events;
using Marten.Events.Protected;
using Marten.Testing.Harness;
using Xunit;
using Shouldly;

namespace EventSourcingTests;

public class protecting_sensitive_data : OneOffConfigurationsContext
{
    [Fact]
    public async Task overwrite_event_data_without_headers()
    {
        var questId = Guid.NewGuid();

        var started = new QuestStarted
        {
            /*Id = questId,*/
            Name = "Destroy the One Ring"
        };
        var joined1 = new MembersJoined(1, "Hobbiton", "Frodo", "Merry");

        theSession.Events.StartStream<Quest>(questId, started, joined1);
        await theSession.SaveChangesAsync();

        var e = (IEvent<QuestStarted>)(await theSession.Events.FetchStreamAsync(questId))[0];

        e.Data.Name = "Just go home instead";

        theSession.Events.OverwriteEvent(e);
        await theSession.SaveChangesAsync();

        var copy = (await theSession.Events.LoadAsync(e.Id)).ShouldBeOfType<Event<QuestStarted>>();
        copy.Data.Name.ShouldBe("Just go home instead");
    }

    [Fact]
    public async Task overwrite_event_data_with_headers()
    {
        StoreOptions(opts =>
        {
            opts.Events.MetadataConfig.HeadersEnabled = true;
        });

        var questId = Guid.NewGuid();

        var started = new QuestStarted
        {
            /*Id = questId,*/
            Name = "Destroy the One Ring"
        };
        var joined1 = new MembersJoined(1, "Hobbiton", "Frodo", "Merry");

        theSession.Events.StartStream<Quest>(questId, started, joined1);
        await theSession.SaveChangesAsync();

        var e = (IEvent<QuestStarted>)(await theSession.Events.FetchStreamAsync(questId))[0];

        e.Data.Name = "Just go home instead";
        e.Headers ??= new();
        e.Headers["Timestamp"] = DateTimeOffset.UtcNow;

        theSession.Events.OverwriteEvent(e);
        await theSession.SaveChangesAsync();

        var copy = (await theSession.Events.LoadAsync(e.Id)).ShouldBeOfType<Event<QuestStarted>>();
        copy.Data.Name.ShouldBe("Just go home instead");
        copy.Headers.ContainsKey("Timestamp");
    }
}
