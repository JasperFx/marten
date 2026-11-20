using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten.Events;
using Marten.Testing.Harness;
using Npgsql;
using Weasel.Postgresql;
using Xunit;
using Shouldly;

namespace EventSourcingTests.QuickAppend;

public class quick_append_end_to_end : OneOffConfigurationsContext
{
    public quick_append_end_to_end()
    {
        StoreOptions(opts =>
        {
            opts.Events.AppendMode = EventAppendMode.Quick;
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

    [Theory]
    [InlineData(EventAppendMode.Quick)]
    [InlineData(EventAppendMode.QuickWithServerTimestamps)]
    [InlineData(EventAppendMode.Rich)]
    public async Task an_event_without_headers_stores_sql_null_rather_than_json_null(EventAppendMode mode)
    {
        // No SetHeader on this session, so the events carry no headers. The quick append paths used to
        // serialize that null into the jsonb literal `null`, and reading it back then needed the
        // serializer to know Dictionary<string, object> even though there was nothing to read. That is
        // what broke the AOT smoke's DCB reads once headers were on by default.
        StoreOptions(opts =>
        {
            opts.Events.AppendMode = mode;
            opts.Events.MetadataConfig.HeadersEnabled = true;
        });

        var streamId = Guid.NewGuid();
        theSession.Events.StartStream<Quest>(streamId, new QuestStarted());
        await theSession.SaveChangesAsync();

        theSession.Events.Append(streamId, new MembersDeparted { Members = ["Frodo"] });
        await theSession.SaveChangesAsync();

        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        var stored = (long)(await conn
            .CreateCommand(
                $"select count(*) from {theStore.Options.Events.DatabaseSchemaName}.mt_events where stream_id = '{streamId}' and headers is not null")
            .ExecuteScalarAsync())!;
        stored.ShouldBe(0);

        var events = await theSession.Events.FetchStreamAsync(streamId);
        events.Count.ShouldBe(2);
        events.ShouldAllBe(e => e.Headers == null);
    }
}
