using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.SchemaChange;

// #4680 — when Upcast<TOld, TNew> is registered AND a TOld instance is appended (typed)
// into the same DocumentStore, the read path bypasses the upcaster and returns TOld.
//
// Mechanism (pre-fix):
//   * Upcast(typeof(TNew), oldEventName, transformation) builds an EventMapping<TNew>
//     keyed under oldEventName in _byEventName. ReadEventData uses the JsonTransformation
//     to produce TNew from the JSON.
//   * Typed Append(TOld) goes through EventMappingFor(typeof(TOld)), which lazily creates
//     an EventMapping<TOld> (Storage.AddMapping). The new TOld mapping also lives in the
//     mapping registry.
//   * On Resolve, the by-name lookup returns the upcaster mapping (good), but then the
//     dotnet_type column read fires the alt-mapping swap: TryGetRegisteredMappingForDotNetTypeName
//     finds the EventMapping<TOld> the typed Append created and replaces the upcaster.
//     The deserializer then materialises TOld and the upcaster is bypassed.
//
// Fix pins:
//   * Reading back from the SAME store after a typed append returns TNew, not TOld.
//   * The original existing-mapping-swap behavior (same eventTypeName, different
//     DotNetTypeName) still works for the non-upcaster case — covered by the
//     `alt_mapping_swap_still_works_when_no_upcaster_registered` test below.
public class Bug_4680_upcaster_shadowed_by_typed_append: BugIntegrationContext
{
    public record UpcastReproOld(string Value);

    public record UpcastReproNew
    {
        public required string Value { get; init; }
    }

    [Fact]
    public async Task same_store_typed_append_is_upcast_on_read()
    {
        StoreOptions(opts =>
        {
            opts.Events.Upcast<UpcastReproOld, UpcastReproNew>(x => new UpcastReproNew { Value = x.Value });
        });

        var streamId = Guid.NewGuid();
        await using (var write = theStore.LightweightSession())
        {
            write.Events.StartStream(streamId, new UpcastReproOld("hello"));
            await write.SaveChangesAsync();
        }

        // Fresh session, reads straight from the database — the cache should not save us.
        await using var read = theStore.LightweightSession();
        var events = await read.Events.FetchStreamAsync(streamId);

        events.Count.ShouldBe(1);
        var data = events[0].Data;
        data.ShouldBeOfType<UpcastReproNew>();
        ((UpcastReproNew)data).Value.ShouldBe("hello");
    }

    // Regression pin for the original purpose of the dotnet_type alt-mapping swap: when
    // two CLR types intentionally share the same EventTypeName (e.g. moved namespaces),
    // the dotnet_type column should still steer the resolver to the right mapping when
    // there is NO upcaster registered for that name. This keeps the fix from over-rotating
    // and breaking the unrelated mapping-versioning use case.
    public record SharedNameVariantA
    {
        public string Label { get; init; } = "";
    }

    public record SharedNameVariantB
    {
        public string Label { get; init; } = "";
    }

    [Fact]
    public async Task alt_mapping_swap_still_works_when_no_upcaster_registered()
    {
        StoreOptions(opts =>
        {
            opts.Events.MapEventType<SharedNameVariantA>("shared_event_name");
            opts.Events.MapEventType<SharedNameVariantB>("shared_event_name");
        });

        var streamId = Guid.NewGuid();
        await using (var write = theStore.LightweightSession())
        {
            write.Events.StartStream(streamId, new SharedNameVariantB { Label = "b-shape" });
            await write.SaveChangesAsync();
        }

        await using var read = theStore.LightweightSession();
        var events = await read.Events.FetchStreamAsync(streamId);

        events.Count.ShouldBe(1);
        // The dotnet_type column resolves to VariantB even though SharedNameVariantA also
        // claims the same EventTypeName. (Marten falls back to whichever mapping was
        // registered first when the dotnet_type column is null; with the column set on
        // append, the resolver should honour it.)
        events[0].Data.ShouldBeOfType<SharedNameVariantB>();
        ((SharedNameVariantB)events[0].Data).Label.ShouldBe("b-shape");
    }
}
