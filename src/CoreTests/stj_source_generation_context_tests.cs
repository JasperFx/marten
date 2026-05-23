using System;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
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
