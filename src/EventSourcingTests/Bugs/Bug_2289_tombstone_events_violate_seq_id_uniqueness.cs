using Marten.Exceptions;
using Marten.Testing.Harness;
using Shouldly;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Events;
using Xunit;
using Xunit.Abstractions;

namespace EventSourcingTests.Bugs;

public class Bug_2289_tombstone_events_violate_seq_id_uniqueness : IntegrationContext
{
    private readonly ITestOutputHelper _output;

    public Bug_2289_tombstone_events_violate_seq_id_uniqueness(ITestOutputHelper output, DefaultStoreFixture fixture) : base(fixture)
    {
        _output = output;
    }

    [Fact]
    public async Task ensure_tombstone_event_has_sequence_set()
    {
        var joined = new MembersJoined { Members = new string[] { "Rand", "Matt", "Perrin", "Thom" } };
        var departed = new MembersDeparted { Members = new[] { "Thom" } };

        var stream = theSession.Events.StartStream<Quest>(joined).Id;
        await theSession.SaveChangesAsync();

        theSession.Events.Append(stream, 2, departed);

        using (var session = theStore.LightweightSession())
        {
            var joined3 = new MembersJoined { Members = new[] { "Egwene" } };
            var departed3 = new MembersDeparted { Members = new[] { "Perrin" } };

            session.Events.Append(stream, joined3, departed3);
            await session.SaveChangesAsync();
        }

        await Should.ThrowAsync<EventStreamUnexpectedMaxEventIdException>(async () =>
        {
            await theSession.SaveChangesAsync();
        });

        var firstSequence = theSession.Events.QueryAllRawEvents()
            .OrderBy(e => e.Sequence)
            .FirstOrDefault();

        firstSequence.Sequence.ShouldNotBe(0);
    }
}
