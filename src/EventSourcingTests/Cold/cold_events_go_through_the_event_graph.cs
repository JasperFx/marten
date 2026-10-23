using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using Marten;
using Marten.Events.Cold;
using Marten.Exceptions;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Cold;

public class cold_events_go_through_the_event_graph: OneOffConfigurationsContext
{
    private readonly InMemoryColdEventStore _cold = new();

    public class RawItemAddedUpcaster: Marten.Services.Json.Transformations.SystemTextJson.EventUpcaster<ItemAdded>
    {
        public override string EventTypeName => "raw_item_added";

        protected override ItemAdded Upcast(JsonDocument oldEventJson) =>
            new(oldEventJson.RootElement.GetProperty("Thing").GetString()!);
    }

    private void configure()
    {
        StoreOptions(opts =>
        {
            opts.UseSystemTextJsonForSerialization();
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Events.UseColdEventStore(_cold);
            opts.Events.Upcast<ItemAddedV1, ItemAdded>("legacy_item_added", old => new ItemAdded(old.Item));
            opts.Events.Upcast(new RawItemAddedUpcaster());
        });
    }

    /// <summary>
    ///     A stream whose cold part holds <paramref name="cold" /> after its opening event, as an old
    ///     application version would have stored them.
    /// </summary>
    private async Task<string> streamWithColdPart(params ColdEvent[] cold)
    {
        var key = Guid.NewGuid().ToString();
        await using var session = theStore.LightweightSession();
        session.Events.StartStream<Cart>(key,
            new object[] { new CartOpened("anne") }.Concat(cold.Select(_ => new ItemAdded("placeholder"))).ToArray());
        await session.SaveChangesAsync();

        var opened = (await session.Events.FetchStreamAsync(key, version: 1)).Single();
        _cold.Store(StorageConstants.DefaultTenantId, key, [opened.ToColdEvent(theStore), ..cold]);

        session.Events.MarkStreamCold(key, cold.Length + 1);
        await session.SaveChangesAsync();
        return key;
    }

    private static ColdEvent stored(long version, string eventTypeName, string json, string? dotnetTypeName = null) => new()
    {
        Id = Guid.NewGuid(),
        Version = version,
        Sequence = version,
        EventTypeName = eventTypeName,
        DotNetTypeName = dotnetTypeName,
        Timestamp = DateTimeOffset.UtcNow,
        Data = json
    };

    [Fact]
    public async Task upcasters_run_on_cold_events()
    {
        configure();
        var key = await streamWithColdPart(
            stored(2, "legacy_item_added", """{"Item":"apple"}"""),
            stored(3, "raw_item_added", """{"Thing":"pear"}"""));

        await using var session = theStore.LightweightSession();

        var stream = await session.Events.FetchForWriting<Cart>(key);
        stream.Aggregate!.Items.ShouldBe(["apple", "pear"]);
        stream.StartingVersion.ShouldBe(3);
    }

    [Fact]
    public async Task the_stored_dotnet_type_name_is_the_fallback_for_an_unknown_event_type_name()
    {
        configure();
        var key = await streamWithColdPart(
            stored(2, "renamed_long_ago", """{"Name":"apple"}""", typeof(ItemAdded).AssemblyQualifiedName));

        await using var session = theStore.LightweightSession();
        (await session.Events.FetchLatest<Cart>(key))!.Items.ShouldBe(["apple"]);
    }

    [Fact]
    public async Task an_event_type_nobody_knows_fails_clearly()
    {
        configure();
        var key = await streamWithColdPart(stored(2, "nobody_knows", "{}"));

        await using var session = theStore.LightweightSession();
        var e = await Should.ThrowAsync<UnknownEventTypeException>(() => session.Events.FetchForWriting<Cart>(key));
        e.Message.ShouldContain("nobody_knows");
    }

    [Fact]
    public async Task an_event_held_anywhere_can_be_deserialized_through_the_store()
    {
        configure();
        var id = Guid.NewGuid();

        var e = await theStore.Events.DeserializeAsync(new ColdEvent
        {
            Id = id,
            Version = 7,
            Sequence = 70,
            EventTypeName = "legacy_item_added",
            Data = """{"Item":"apple"}""",
            StreamKey = "cart-1",
            TenantId = "tenant-a",
            CorrelationId = "corr",
            Headers = new Dictionary<string, object> { ["source"] = "cold" }
        });

        e.Data.ShouldBe(new ItemAdded("apple"));
        e.ShouldBeAssignableTo<IEvent<ItemAdded>>();
        e.Id.ShouldBe(id);
        e.Version.ShouldBe(7);
        e.Sequence.ShouldBe(70);
        e.StreamKey.ShouldBe("cart-1");
        e.TenantId.ShouldBe("tenant-a");
        e.CorrelationId.ShouldBe("corr");
        e.GetHeader("source").ShouldBe("cold");
    }
}
