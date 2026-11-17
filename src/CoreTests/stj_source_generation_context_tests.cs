using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Marten;
using Marten.Exceptions;
using Marten.Services;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace CoreTests;

// marten#4540 (Item 2): opt in to System.Text.Json source generation by layering a
// JsonSerializerContext ahead of the reflection resolver. Types in the context use its
// precompiled metadata (no first-serialize reflection cost / AOT-clean); everything else
// falls back to reflection. This test proves the opt-in is wired through every internal
// JsonSerializerOptions (serialize + deserialize) and that the reflection fallback is
// preserved for types the context doesn't cover.
public class stj_source_generation_context_tests : BugIntegrationContext
{
    [Fact]
    public async Task round_trips_covered_and_uncovered_document_types()
    {
        StoreOptions(opts =>
        {
            var serializer = new SystemTextJsonSerializer();
            serializer.UseTypeInfoResolver(StjOptInContext.Default);
            opts.Serializer(serializer);
        });

        // Sanity: the context covers the one type and not the other
        StjOptInContext.Default.GetTypeInfo(typeof(StjCoveredDoc)).ShouldNotBeNull();
        StjOptInContext.Default.GetTypeInfo(typeof(StjUncoveredDoc)).ShouldBeNull();

        var covered = new StjCoveredDoc { Id = Guid.NewGuid(), Name = "covered", Count = 7 };
        var uncovered = new StjUncoveredDoc { Id = Guid.NewGuid(), Description = "reflection-fallback" };

        theSession.Store(covered);
        theSession.Store(uncovered);
        await theSession.SaveChangesAsync();

        // Fresh session so the values are genuinely deserialized from the database
        await using var query = theStore.QuerySession();

        var loadedCovered = await query.LoadAsync<StjCoveredDoc>(covered.Id);
        loadedCovered.ShouldNotBeNull();
        loadedCovered.Name.ShouldBe("covered");
        loadedCovered.Count.ShouldBe(7);

        var loadedUncovered = await query.LoadAsync<StjUncoveredDoc>(uncovered.Id);
        loadedUncovered.ShouldNotBeNull();
        loadedUncovered.Description.ShouldBe("reflection-fallback");
    }

    // marten#5624: assigning TypeInfoResolver replaces the reflection resolver instead of layering
    // onto it, and then the anonymous type a Select() projection materializes into has no metadata
    // anywhere -- and cannot be given any, since [JsonSerializable] needs a nameable type. The raw
    // System.Text.Json error advises an impossible annotation, so Marten explains the two remedies
    // instead. Nothing about this is AOT-specific; it fails under a JIT exactly the same way.
    [Fact]
    public async Task anonymous_projection_is_refused_when_the_context_replaced_reflection()
    {
        StoreOptions(opts => opts.UseSystemTextJsonForSerialization(
            new JsonSerializerOptions { TypeInfoResolver = StjOptInContext.Default }));

        theSession.Store(new StjCoveredDoc { Id = Guid.NewGuid(), Name = "covered", Count = 7 });
        await theSession.SaveChangesAsync();

        await using var query = theStore.QuerySession();

        var ex = await Should.ThrowAsync<MartenNotSupportedException>(() =>
            query.Query<StjCoveredDoc>().Select(x => new { x.Name, x.Count }).ToListAsync());

        ex.Message.ShouldContain("anonymous type");
        ex.Message.ShouldContain("UseTypeInfoResolver");
        ex.InnerException.ShouldBeOfType<NotSupportedException>();
    }

    [Fact]
    public async Task anonymous_projection_works_when_the_context_is_layered_instead()
    {
        StoreOptions(opts =>
        {
            var serializer = new SystemTextJsonSerializer();
            serializer.UseTypeInfoResolver(StjOptInContext.Default);
            opts.Serializer(serializer);
        });

        var id = Guid.NewGuid();
        theSession.Store(new StjCoveredDoc { Id = id, Name = "covered", Count = 7 });
        await theSession.SaveChangesAsync();

        await using var query = theStore.QuerySession();

        var results = await query.Query<StjCoveredDoc>().Where(x => x.Id == id)
            .Select(x => new { x.Name, x.Count }).ToListAsync();

        results.Count.ShouldBe(1);
        results[0].Name.ShouldBe("covered");
        results[0].Count.ShouldBe(7);
    }
}

public class StjCoveredDoc
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class StjUncoveredDoc
{
    public Guid Id { get; set; }
    public string Description { get; set; } = string.Empty;
}

[JsonSerializable(typeof(StjCoveredDoc))]
internal partial class StjOptInContext : JsonSerializerContext
{
}
