using System;
using System.Threading.Tasks;
using Marten;
using Marten.Services;
using Marten.Internal.ClosedShape;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace CoreTests.Storage.Identification;

/// <summary>
/// W3 spike M2: validates that the 4 closed-shape storage classes
/// (QueryOnly / Lightweight / IdentityMap / DirtyTracking) each plug
/// into the matching <c>DocumentTracking</c> session mode and exhibit
/// the right semantics (identity-map short-circuit, dirty tracking,
/// etc.).
/// </summary>
public class closed_shape_storage_styles_tests: BugIntegrationContext
{
    [Fact]
    public async Task query_session_resolves_via_query_only_storage()
    {
        theStore.UseLightweightSequentialGuidClosedShape<StyleDoc>();

        var id = Guid.NewGuid();
        await using (var session = theStore.LightweightSession())
        {
            session.Store(new StyleDoc { Id = id, Name = "queried" });
            await session.SaveChangesAsync();
        }

        // QuerySession picks DocumentTracking.QueryOnly → QueryOnly storage.
        // The QueryOnly selector reads data at col 0 (id excluded).
        await using var query = theStore.QuerySession();
        var loaded = await query.LoadAsync<StyleDoc>(id);
        loaded.ShouldNotBeNull();
        loaded.Name.ShouldBe("queried");
    }

    [Fact]
    public async Task identity_map_session_returns_same_instance_on_repeated_loads()
    {
        theStore.UseLightweightSequentialGuidClosedShape<StyleDoc>();

        var id = Guid.NewGuid();
        await using (var session = theStore.LightweightSession())
        {
            session.Store(new StyleDoc { Id = id, Name = "shared" });
            await session.SaveChangesAsync();
        }

        // IdentitySession (DocumentTracking.IdentityOnly) → IdentityMap
        // storage. The selector populates the identity map on first load;
        // subsequent loads short-circuit to the cached instance.
        await using var session2 = theStore.IdentitySession();
        var first = await session2.LoadAsync<StyleDoc>(id);
        var second = await session2.LoadAsync<StyleDoc>(id);

        first.ShouldNotBeNull();
        second.ShouldNotBeNull();
        ReferenceEquals(first, second).ShouldBeTrue();
    }

    [Fact]
    public async Task lightweight_session_returns_fresh_instance_on_repeated_loads()
    {
        theStore.UseLightweightSequentialGuidClosedShape<StyleDoc>();

        var id = Guid.NewGuid();
        await using (var session = theStore.LightweightSession())
        {
            session.Store(new StyleDoc { Id = id, Name = "no-cache" });
            await session.SaveChangesAsync();
        }

        // LightweightSession skips identity-map writes — every LoadAsync
        // round-trips to the DB and returns a freshly-deserialized
        // instance.
        await using var session2 = theStore.LightweightSession();
        var first = await session2.LoadAsync<StyleDoc>(id);
        var second = await session2.LoadAsync<StyleDoc>(id);

        first.ShouldNotBeNull();
        second.ShouldNotBeNull();
        ReferenceEquals(first, second).ShouldBeFalse();
    }

    [Fact]
    public async Task dirty_tracking_session_persists_in_place_modifications_to_loaded_docs()
    {
        theStore.UseLightweightSequentialGuidClosedShape<StyleDoc>();

        var id = Guid.NewGuid();
        await using (var session = theStore.LightweightSession())
        {
            session.Store(new StyleDoc { Id = id, Name = "v1" });
            await session.SaveChangesAsync();
        }

        // DirtyTrackedSession → DirtyTracking storage → DirtyTracking
        // selector registers a ChangeTracker per loaded doc.
        // SaveChangesAsync dirty-checks every tracker, detects the
        // in-place mutation, and persists the change without an
        // explicit Update call.
        await using (var session = theStore.DirtyTrackedSession())
        {
            var loaded = await session.LoadAsync<StyleDoc>(id);
            loaded.ShouldNotBeNull();
            loaded.Name = "v2-mutated-in-place";
            await session.SaveChangesAsync();
        }

        await using var query = theStore.QuerySession();
        var reloaded = await query.LoadAsync<StyleDoc>(id);
        reloaded.ShouldNotBeNull();
        reloaded.Name.ShouldBe("v2-mutated-in-place");
    }

    [Fact]
    public async Task identity_session_load_returns_session_stored_instance_without_db_hit()
    {
        theStore.UseLightweightSequentialGuidClosedShape<StyleDoc>();

        // Store + LoadAsync in the same session — identity-map storage's
        // Store() writes to the map at Store time, so the subsequent
        // LoadAsync should return the stored instance, even before
        // SaveChangesAsync writes it to the DB.
        var doc = new StyleDoc { Id = Guid.NewGuid(), Name = "in-session" };

        await using var session = theStore.IdentitySession();
        session.Store(doc);

        var loaded = await session.LoadAsync<StyleDoc>(doc.Id);
        loaded.ShouldNotBeNull();
        ReferenceEquals(doc, loaded).ShouldBeTrue();
    }
}

public class StyleDoc
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}
