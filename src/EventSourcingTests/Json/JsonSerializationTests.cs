using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Json;

public class JsonSerializationTests: IntegrationContext
{
    [Fact]
    public async Task ForNonPublicMembersStorageAll_ShouldAppendAndFetchEventOfRecordTypeWithASingleProperty()
    {
        StoreOptions(options => options.UseDefaultSerialization(nonPublicMembersStorage: NonPublicMembersStorage.All));

        // Given
        var streamId = Guid.NewGuid();
        var @event = new EventWithASingleProperty(Guid.NewGuid());

        // When
        theSession.Events.Append(streamId, @event);
        await theSession.SaveChangesAsync();

        var events = await theSession.Events.FetchStreamAsync(streamId);
        var fetchedEvent = events.Select(e => e.Data).OfType<EventWithASingleProperty>()
            .ShouldHaveSingleItem();
        fetchedEvent.ShouldBe(@event);
    }

    public JsonSerializationTests(DefaultStoreFixture fixture) : base(fixture)
    {
    }
}

public record EventWithASingleProperty(Guid SingleProperty);
