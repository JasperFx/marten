using System;
using System.Threading.Tasks;
using Marten;
using Marten.Schema;
using Marten.Storage.Identification.ClosedShape;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace CoreTests.Storage.Identification;

/// <summary>
/// W3 spike M12 – M15: closed-shape identification strategies for the
/// remaining id types — HiLo int / HiLo long / IdentityKey / random
/// GUID / strong-typed wrappers. All routed via the default-on flag
/// rather than the explicit per-strategy registration methods.
/// </summary>
public class closed_shape_id_strategies_tests: BugIntegrationContext
{
    private DocumentStore ClosedShapeStore(Action<StoreOptions>? extra = null)
        => StoreOptions(opts =>
        {
            opts.UseClosedShapeDocumentStorage = true;
            extra?.Invoke(opts);
        });

    // ----- M12: HiLo int -----

    [Fact]
    public async Task hilo_int_assigns_a_positive_id_on_store()
    {
        var store = ClosedShapeStore();

        var doc = new HiloIntDoc { Name = "v1" };
        await using var session = store.LightweightSession();
        session.Store(doc);
        await session.SaveChangesAsync();

        doc.Id.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task hilo_int_round_trips()
    {
        var store = ClosedShapeStore();

        var doc = new HiloIntDoc { Name = "fresh" };
        await using (var session = store.LightweightSession())
        {
            session.Store(doc);
            await session.SaveChangesAsync();
        }

        await using var query = store.QuerySession();
        var loaded = await query.LoadAsync<HiloIntDoc>(doc.Id);
        loaded.ShouldNotBeNull();
        loaded.Name.ShouldBe("fresh");
    }

    // ----- M12: HiLo long -----

    [Fact]
    public async Task hilo_long_assigns_a_positive_id_on_store()
    {
        var store = ClosedShapeStore();

        var doc = new HiloLongDoc { Name = "v1" };
        await using var session = store.LightweightSession();
        session.Store(doc);
        await session.SaveChangesAsync();

        doc.Id.ShouldBeGreaterThan(0L);
    }

    // ----- M13: IdentityKey -----

    [Fact]
    public async Task identity_key_assigns_alias_slash_sequence()
    {
        var store = ClosedShapeStore(opts =>
        {
            opts.Schema.For<IdentityKeyDoc>().IdStrategy(
                new Marten.Schema.Identity.Sequences.IdentityKeyGeneration(
                    (DocumentMapping)null!, new Marten.Schema.Identity.Sequences.HiloSettings()));
        });

        var doc = new IdentityKeyDoc { Name = "v1" };
        await using var session = store.LightweightSession();
        session.Store(doc);
        await session.SaveChangesAsync();

        doc.Id.ShouldNotBeNullOrEmpty();
        // The mapping's alias defaults to the type name lowercased.
        doc.Id.ShouldStartWith("identitykeydoc/");
    }

    // ----- M14: Custom (random) GUID -----

    [Fact]
    public async Task random_guid_assigns_NewGuid_when_empty()
    {
        var store = ClosedShapeStore(opts =>
        {
            opts.Schema.For<RandomGuidDoc>().IdStrategy(new Marten.Schema.Identity.GuidIdGeneration());
        });

        var doc = new RandomGuidDoc { Name = "v1" };
        await using var session = store.LightweightSession();
        session.Store(doc);
        await session.SaveChangesAsync();

        doc.Id.ShouldNotBe(Guid.Empty);
    }

    // ----- M15: Strong-typed IDs -----

    [Fact]
    public async Task strong_typed_guid_id_round_trips()
    {
        var store = ClosedShapeStore(opts =>
        {
            opts.RegisterValueType(typeof(StrongGuidId));
        });

        var doc = new StrongGuidIdDoc { Name = "v1" };
        await using (var session = store.LightweightSession())
        {
            session.Store(doc);
            await session.SaveChangesAsync();
        }
        doc.Id.Value.ShouldNotBe(Guid.Empty);

        await using var query = store.QuerySession();
        var loaded = await query.LoadAsync<StrongGuidIdDoc>(doc.Id);
        loaded.ShouldNotBeNull();
        loaded.Name.ShouldBe("v1");
    }

    [Fact]
    public async Task strong_typed_long_id_advances_via_hilo()
    {
        var store = ClosedShapeStore(opts =>
        {
            opts.RegisterValueType(typeof(StrongLongId));
        });

        var first = new StrongLongIdDoc { Name = "v1" };
        var second = new StrongLongIdDoc { Name = "v2" };
        await using var session = store.LightweightSession();
        session.Store(first);
        session.Store(second);
        await session.SaveChangesAsync();

        first.Id.Value.ShouldBeGreaterThan(0L);
        second.Id.Value.ShouldBeGreaterThan(0L);
        first.Id.Value.ShouldNotBe(second.Id.Value);
    }

    // ----- IsSupported predicate coverage -----

    [Fact]
    public void IsSupported_accepts_each_id_strategy()
    {
        var store = ClosedShapeStore(opts =>
        {
            opts.Schema.For<RandomGuidDoc>().IdStrategy(new Marten.Schema.Identity.GuidIdGeneration());
            opts.Schema.For<IdentityKeyDoc>().IdStrategy(
                new Marten.Schema.Identity.Sequences.IdentityKeyGeneration(
                    (DocumentMapping)null!, new Marten.Schema.Identity.Sequences.HiloSettings()));
            opts.RegisterValueType(typeof(StrongGuidId));
        });

        bool sup(Type t) => ClosedShapeRegistration.IsSupported(
            (DocumentMapping)store.Options.Storage.FindMapping(t));

        sup(typeof(HiloIntDoc)).ShouldBeTrue();
        sup(typeof(HiloLongDoc)).ShouldBeTrue();
        sup(typeof(IdentityKeyDoc)).ShouldBeTrue();
        sup(typeof(RandomGuidDoc)).ShouldBeTrue();
        sup(typeof(StrongGuidIdDoc)).ShouldBeTrue();
    }
}

public class HiloIntDoc
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class HiloLongDoc
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class IdentityKeyDoc
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public class RandomGuidDoc
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public record struct StrongGuidId(Guid Value);

public class StrongGuidIdDoc
{
    public StrongGuidId Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public record struct StrongLongId(long Value);

public class StrongLongIdDoc
{
    public StrongLongId Id { get; set; }
    public string Name { get; set; } = string.Empty;
}
