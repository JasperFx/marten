using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Storage.Identification.ClosedShape;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace CoreTests.Storage.Identification;

/// <summary>
/// W3 spike end-to-end validation: prove that a hand-written closed-shape
/// <c>DocumentStorage&lt;TDoc, TId&gt;</c> can drive Marten's basic
/// document-DB features without going through the runtime Roslyn codegen
/// path. The storage class
/// (<see cref="LightweightSequentialGuidStorage{TDoc}"/>) is registered
/// via <see cref="ClosedShapeRegistration"/> ahead of any session, which
/// bypasses <c>ProviderGraph</c>'s codegen-emit branch for the target
/// document type.
/// </summary>
/// <remarks>
/// Tracking: jasperfx/marten#4404 (W3). The closed-shape storage class
/// composes with <see cref="Marten.Storage.Identification.SequentialGuidIdentification{TDoc}"/>
/// for identity assignment, and emits raw <c>INSERT … ON CONFLICT</c> SQL
/// instead of calling the per-document <c>mt_upsert_*</c> function — the
/// PostgreSQL function infrastructure is part of what the closed-shape
/// rewrite drops.
/// </remarks>
public class closed_shape_storage_spike_tests: BugIntegrationContext
{
    [Fact]
    public async Task store_save_load_round_trip()
    {
        StoreOptions(opts =>
        {
            // Drop metadata columns so the table is just (id, data) —
            // matches the SQL the spike operation emits. Metadata-column
            // support joins the closed-shape hierarchy when the
            // configuration matrix expands; the spike validates the
            // pattern, not the matrix.
            opts.Policies.DisableInformationalFields();
        });

        theStore.UseLightweightSequentialGuidClosedShape<SpikeDoc>();

        var id = Guid.NewGuid();
        await using (var session = theStore.LightweightSession())
        {
            session.Store(new SpikeDoc { Id = id, Name = "alpha" });
            await session.SaveChangesAsync();
        }

        await using var query = theStore.QuerySession();
        var loaded = await query.LoadAsync<SpikeDoc>(id);

        loaded.ShouldNotBeNull();
        loaded.Id.ShouldBe(id);
        loaded.Name.ShouldBe("alpha");
    }

    [Fact]
    public async Task store_assigns_a_sequential_guid_when_id_is_empty()
    {
        StoreOptions(opts =>
        {
            opts.Policies.DisableInformationalFields();
        });

        theStore.UseLightweightSequentialGuidClosedShape<SpikeDoc>();

        var doc = new SpikeDoc { Name = "no-id-yet" };
        doc.Id.ShouldBe(Guid.Empty);

        await using (var session = theStore.LightweightSession())
        {
            session.Store(doc);
            // IIdentification.AssignIfMissing has run by now (Store →
            // AssignIdentity); the original instance's Id must reflect
            // the assigned value.
            doc.Id.ShouldNotBe(Guid.Empty);
            await session.SaveChangesAsync();
        }

        await using var query = theStore.QuerySession();
        var loaded = await query.LoadAsync<SpikeDoc>(doc.Id);
        loaded.ShouldNotBeNull();
        loaded.Name.ShouldBe("no-id-yet");
    }

    [Fact]
    public async Task linq_query_returns_documents_persisted_via_closed_shape_storage()
    {
        StoreOptions(opts =>
        {
            opts.Policies.DisableInformationalFields();
        });

        theStore.UseLightweightSequentialGuidClosedShape<SpikeDoc>();

        await using (var session = theStore.LightweightSession())
        {
            session.Store(new SpikeDoc { Id = Guid.NewGuid(), Name = "alpha" });
            session.Store(new SpikeDoc { Id = Guid.NewGuid(), Name = "beta" });
            session.Store(new SpikeDoc { Id = Guid.NewGuid(), Name = "gamma" });
            await session.SaveChangesAsync();
        }

        await using var query = theStore.QuerySession();
        var betas = await query.Query<SpikeDoc>()
            .Where(x => x.Name == "beta")
            .ToListAsync();

        betas.Count.ShouldBe(1);
        betas[0].Name.ShouldBe("beta");
    }

    [Fact]
    public async Task delete_via_session_removes_the_row()
    {
        StoreOptions(opts =>
        {
            opts.Policies.DisableInformationalFields();
        });

        theStore.UseLightweightSequentialGuidClosedShape<SpikeDoc>();

        var id = Guid.NewGuid();
        await using (var session = theStore.LightweightSession())
        {
            session.Store(new SpikeDoc { Id = id, Name = "doomed" });
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            session.Delete<SpikeDoc>(id);
            await session.SaveChangesAsync();
        }

        await using var query = theStore.QuerySession();
        var loaded = await query.LoadAsync<SpikeDoc>(id);
        loaded.ShouldBeNull();
    }

    [Fact]
    public async Task upsert_overwrites_on_second_store_of_same_id()
    {
        StoreOptions(opts =>
        {
            opts.Policies.DisableInformationalFields();
        });

        theStore.UseLightweightSequentialGuidClosedShape<SpikeDoc>();

        var id = Guid.NewGuid();
        await using (var session = theStore.LightweightSession())
        {
            session.Store(new SpikeDoc { Id = id, Name = "original" });
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            session.Store(new SpikeDoc { Id = id, Name = "updated" });
            await session.SaveChangesAsync();
        }

        await using var query = theStore.QuerySession();
        var loaded = await query.LoadAsync<SpikeDoc>(id);
        loaded.ShouldNotBeNull();
        loaded.Name.ShouldBe("updated");
    }
}

public class SpikeDoc
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}
