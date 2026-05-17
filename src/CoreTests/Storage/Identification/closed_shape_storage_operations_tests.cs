using System;
using System.Threading.Tasks;
using Marten;
using Marten.Exceptions;
using Marten.Internal.ClosedShape;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace CoreTests.Storage.Identification;

/// <summary>
/// W3 spike M3: validates the operation matrix on the closed-shape
/// document storage. Insert / Update / Upsert each emit distinct SQL
/// and have distinct postprocess semantics — Insert throws on
/// collision, Update throws on missing-row, Upsert is fire-and-forget.
/// </summary>
public class closed_shape_storage_operations_tests: BugIntegrationContext
{
    [Fact]
    public async Task insert_persists_a_new_document()
    {
        theStore.UseLightweightSequentialGuidClosedShape<OpDoc>();

        var id = Guid.NewGuid();
        await using (var session = theStore.LightweightSession())
        {
            session.Insert(new OpDoc { Id = id, Name = "fresh" });
            await session.SaveChangesAsync();
        }

        await using var query = theStore.QuerySession();
        var loaded = await query.LoadAsync<OpDoc>(id);
        loaded.ShouldNotBeNull();
        loaded.Name.ShouldBe("fresh");
    }

    [Fact]
    public async Task insert_throws_when_id_already_exists()
    {
        theStore.UseLightweightSequentialGuidClosedShape<OpDoc>();

        var id = Guid.NewGuid();
        await using (var session = theStore.LightweightSession())
        {
            session.Insert(new OpDoc { Id = id, Name = "first" });
            await session.SaveChangesAsync();
        }

        // ON CONFLICT DO NOTHING + RETURNING id → no row back → operation
        // raises DocumentAlreadyExistsException.
        await using (var session = theStore.LightweightSession())
        {
            session.Insert(new OpDoc { Id = id, Name = "second" });
            await Should.ThrowAsync<DocumentAlreadyExistsException>(
                () => session.SaveChangesAsync());
        }

        // Original row should be untouched (the ON CONFLICT DO NOTHING
        // means no write happened).
        await using var query = theStore.QuerySession();
        var loaded = await query.LoadAsync<OpDoc>(id);
        loaded.ShouldNotBeNull();
        loaded.Name.ShouldBe("first");
    }

    [Fact]
    public async Task update_persists_changes_to_an_existing_document()
    {
        theStore.UseLightweightSequentialGuidClosedShape<OpDoc>();

        var id = Guid.NewGuid();
        await using (var session = theStore.LightweightSession())
        {
            session.Insert(new OpDoc { Id = id, Name = "before" });
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            session.Update(new OpDoc { Id = id, Name = "after" });
            await session.SaveChangesAsync();
        }

        await using var query = theStore.QuerySession();
        var loaded = await query.LoadAsync<OpDoc>(id);
        loaded.ShouldNotBeNull();
        loaded.Name.ShouldBe("after");
    }

    [Fact]
    public async Task update_throws_when_no_row_with_the_id_exists()
    {
        theStore.UseLightweightSequentialGuidClosedShape<OpDoc>();

        var id = Guid.NewGuid();
        await using (var session = theStore.LightweightSession())
        {
            session.Update(new OpDoc { Id = id, Name = "never-existed" });
            await Should.ThrowAsync<NonExistentDocumentException>(
                () => session.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task upsert_works_for_both_new_and_existing_documents()
    {
        theStore.UseLightweightSequentialGuidClosedShape<OpDoc>();

        var id = Guid.NewGuid();

        // First save — new row. Upsert succeeds.
        await using (var session = theStore.LightweightSession())
        {
            session.Store(new OpDoc { Id = id, Name = "v1" });
            await session.SaveChangesAsync();
        }

        // Second save — existing row. Upsert succeeds, replaces data.
        await using (var session = theStore.LightweightSession())
        {
            session.Store(new OpDoc { Id = id, Name = "v2" });
            await session.SaveChangesAsync();
        }

        await using var query = theStore.QuerySession();
        var loaded = await query.LoadAsync<OpDoc>(id);
        loaded.ShouldNotBeNull();
        loaded.Name.ShouldBe("v2");
    }
}

public class OpDoc
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}
