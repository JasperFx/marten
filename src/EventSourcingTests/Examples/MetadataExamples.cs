using System;
using System.Diagnostics;
using System.Threading.Tasks;
using EventSourcingTests.Projections;
using JasperFx.Core;
using JasperFx.Events;
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace EventSourcingTests.Examples;

public static class MetadataExamples
{
    #region sample_overriding_event_metadata_by_position

    public static async Task override_metadata(IDocumentSession session)
    {
        var started = new QuestStarted { Name = "Find the Orb" };

        var joined = new MembersJoined
        {
            Day = 2, Location = "Faldor's Farm", Members = new string[] { "Garion", "Polgara", "Belgarath" }
        };

        var slayed1 = new MonsterSlayed { Name = "Troll" };
        var slayed2 = new MonsterSlayed { Name = "Dragon" };

        var joined2 = new MembersJoined { Day = 5, Location = "Sendaria", Members = new string[] { "Silk", "Barak" } };

        var action = session.Events
            .StartStream<QuestParty>(started, joined, slayed1, slayed2, joined2);

        // I'm grabbing the IEvent wrapper for the first event in the action
        var wrapper = action.Events[0];
        wrapper.Timestamp = DateTimeOffset.UtcNow.Subtract(1.Hours());
        wrapper.SetHeader("category", "important");
        wrapper.Id = Guid.NewGuid(); // Just showing that you *can* override this value
        wrapper.CausationId = wrapper.CorrelationId = Activity.Current?.Id;

        await session.SaveChangesAsync();
    }

    #endregion

    #region sample_override_by_appending_the_event_wrapper

    public static async Task override_metadata2(IDocumentSession session)
    {
        var started = new QuestStarted { Name = "Find the Orb" };

        var joined = new MembersJoined
        {
            Day = 2, Location = "Faldor's Farm", Members = new string[] { "Garion", "Polgara", "Belgarath" }
        };

        var slayed1 = new MonsterSlayed { Name = "Troll" };
        var slayed2 = new MonsterSlayed { Name = "Dragon" };

        var joined2 = new MembersJoined { Day = 5, Location = "Sendaria", Members = new string[] { "Silk", "Barak" } };

        // The result of this is an IEvent wrapper around the
        // started data with an overridden timestamp
        // and a value for the "color" header
        var wrapper = started.AsEvent()
            .AtTimestamp(DateTimeOffset.UtcNow.Subtract(1.Hours()))
            .WithHeader("color", "blue");

        session.Events
            .StartStream<QuestParty>(wrapper, joined, slayed1, slayed2, joined2);

        await session.SaveChangesAsync();
    }

    #endregion

    public static async Task bootstrap_with_quick_append_server_timestamp()
    {
        #region sample_setting_quick_with_server_timestamps

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddMarten(opts =>
        {
            opts.Connection(builder.Configuration.GetConnectionString("marten"));

            // This is important!
            opts.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;
        });

        #endregion
    }
}
