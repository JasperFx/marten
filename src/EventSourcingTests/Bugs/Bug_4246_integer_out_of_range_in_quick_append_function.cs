using System.Threading.Tasks;
using JasperFx.Events;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Core;
using Xunit;

namespace EventSourcingTests.Bugs;

public class Bug_4246_integer_out_of_range_in_quick_append_function : OneOffConfigurationsContext
{
    public Bug_4246_integer_out_of_range_in_quick_append_function()
    {
        StoreOptions(opts =>
        {
            opts.Events.AppendMode = EventAppendMode.Quick;
            opts.Events.EnableBigIntEvents = true;
            opts.Connection(ConnectionSource.ConnectionString);
        });
    }

    [Fact]
    public async Task quick_append_should_handle_sequence_ids_above_int_max_value()
    {
        var schemaName = theStore.Options.DatabaseSchemaName;
        const long expectedFirstSequence = 1L;
        const long expectedSecondSequence = 2_200_000_000L;

        await using var session = theStore.LightweightSession();
        var streamId = session.Events.StartStream<Quest>(new QuestStarted { Name = "Test" });
        await session.SaveChangesAsync();

        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await conn.CreateCommand($"ALTER SEQUENCE {schemaName}.mt_events_sequence RESTART WITH {expectedSecondSequence}")
            .ExecuteNonQueryAsync();
        await conn.CloseAsync();

        await using var session2 = theStore.LightweightSession();
        session2.Events.Append(streamId.Id, new MembersJoined { Members = new[] { "Frodo" } });
        await session2.SaveChangesAsync();

        await using var session3 = theStore.LightweightSession();
        var events = await session3.Events.FetchStreamAsync(streamId.Id);
        events.Count.ShouldBe(2);
        events[0].Sequence.ShouldBe(expectedFirstSequence);
        events[1].Sequence.ShouldBe(expectedSecondSequence);
    }
}
