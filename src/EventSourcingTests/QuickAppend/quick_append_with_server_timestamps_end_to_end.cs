using System.Threading.Tasks;
using JasperFx.Events;
using Marten.Events;
using Marten.Testing.Harness;
using Xunit;
using Shouldly;

namespace EventSourcingTests.QuickAppend;

public class quick_append_end_to_end_with_server_timestamps : OneOffConfigurationsContext
{
    public quick_append_end_to_end_with_server_timestamps()
    {
        StoreOptions(opts =>
        {
            opts.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;
            opts.Events.MetadataConfig.CausationIdEnabled = true;
            opts.Events.MetadataConfig.CorrelationIdEnabled = true;
            opts.Events.MetadataConfig.HeadersEnabled = true;
        });
    }

    [Fact]
    public async Task append_with_metadata_using_function()
    {
        theSession.CorrelationId = "lotr";
        theSession.CausationId = "fellowship";
        theSession.SetHeader("color", "blue");

        var streamId =
            theSession.Events.StartStream<Quest>(new QuestStarted(), new MembersJoined(1, "Hobbiton", "Frodo", "Sam")).Id;
        await theSession.SaveChangesAsync();

        theSession.Events.Append(streamId, new MembersDeparted { Members = new string[] { "Frodo" } });
        await theSession.SaveChangesAsync();

        var events = await theSession.Events.FetchStreamAsync(streamId);

        foreach (var e in events)
        {
            e.Sequence.ShouldBeGreaterThan(0);
            e.Version.ShouldBeGreaterThan(0);

            e.CorrelationId.ShouldBe(theSession.CorrelationId);
            e.CausationId.ShouldBe(theSession.CausationId);
            e.Headers["color"].ShouldBe("blue");
        }
    }

    [Fact]
    public async Task make_sure_the_migration_is_end_to_end()
    {
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        await theStore.Storage.Database.AssertDatabaseMatchesConfigurationAsync();
    }
}
