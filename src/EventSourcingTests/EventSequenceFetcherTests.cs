using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events;
using Marten.Internal.Sessions;
using Marten.Testing;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests;

public class EventSequenceFetcherTests : OneOffConfigurationsContext
{
    [Fact]
    public async Task fetch_sequence_numbers_async()
    {
        await theStore.EnsureStorageExistsAsync(typeof(IEvent));

        await using var query = (QuerySession)theStore.QuerySession();

        var handler = new EventSequenceFetcher(theStore.Events, 5);
        var sequences = (await query.ExecuteHandlerAsync(handler, CancellationToken.None)).ToList();

        sequences.Count.ShouldBe(5);
        for (var i = 0; i < sequences.Count - 1; i++)
        {
            (sequences[i + 1] - sequences[i]).ShouldBe(1);
        }
    }

    [Fact]
    public void fetch_sequence_numbers_sync()
    {
        theStore.Tenancy.Default.Database.EnsureStorageExists(typeof(IEvent));

        using var query = (QuerySession)theStore.QuerySession();

        var handler = new EventSequenceFetcher(theStore.Events, 5);
        var sequences = query.ExecuteHandler(handler).ToList();

        sequences.Count.ShouldBe(5);
        for (var i = 0; i < sequences.Count - 1; i++)
        {
            (sequences[i + 1] - sequences[i]).ShouldBe(1);
        }
    }
}