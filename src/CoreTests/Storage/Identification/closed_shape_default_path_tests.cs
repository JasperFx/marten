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
/// W3 spike M6: validates the <c>StoreOptions.UseClosedShapeDocumentStorage</c>
/// flag. When on, supported document mappings auto-route through the
/// closed-shape path with no explicit registration call; unsupported
/// mappings fall back to codegen so existing configurations keep
/// working.
/// </summary>
public class closed_shape_default_path_tests: BugIntegrationContext
{
    [Fact]
    public async Task default_path_round_trips_a_supported_document()
    {
        var store = StoreOptions(opts =>
        {
            opts.UseClosedShapeDocumentStorage = true;
        });

        var id = Guid.NewGuid();
        await using (var session = store.LightweightSession())
        {
            session.Store(new DefaultPathDoc { Id = id, Name = "auto-routed" });
            await session.SaveChangesAsync();
        }

        await using var query = store.QuerySession();
        var loaded = await query.LoadAsync<DefaultPathDoc>(id);
        loaded.ShouldNotBeNull();
        loaded.Name.ShouldBe("auto-routed");
    }

    [Fact]
    public async Task default_path_falls_back_to_codegen_for_duplicated_fields()
    {
        // Duplicated fields are outside the closed-shape envelope — the
        // flag must NOT prevent the codegen path from kicking in for
        // this mapping. If it does, the store will throw at storage
        // build because the closed-shape path doesn't implement
        // duplicated fields yet.
        var store = StoreOptions(opts =>
        {
            opts.UseClosedShapeDocumentStorage = true;
            opts.Schema.For<DuplicatedFieldDoc>().Duplicate(x => x.Name);
        });

        var id = Guid.NewGuid();
        await using (var session = store.LightweightSession())
        {
            session.Store(new DuplicatedFieldDoc { Id = id, Name = "v1" });
            await session.SaveChangesAsync();
        }

        await using var query = store.QuerySession();
        (await query.LoadAsync<DuplicatedFieldDoc>(id))!.Name.ShouldBe("v1");
    }

    [Fact]
    public void IsSupported_recognises_the_supported_envelope()
    {
        var store = StoreOptions(_ => { });
        var mapping = (DocumentMapping)store.Options.Storage.FindMapping(typeof(DefaultPathDoc));
        ClosedShapeRegistration.IsSupported(mapping).ShouldBeTrue();
    }

    [Fact]
    public void IsSupported_rejects_unsupported_features()
    {
        var store = StoreOptions(opts =>
        {
            opts.Schema.For<DuplicatedFieldDoc>().Duplicate(x => x.Name);
        });

        var dup = (DocumentMapping)store.Options.Storage.FindMapping(typeof(DuplicatedFieldDoc));
        ClosedShapeRegistration.IsSupported(dup).ShouldBeFalse();
    }

    [Fact]
    public void IsSupported_accepts_optimistic_concurrency()
    {
        // M7: optimistic concurrency is inside the coverage envelope.
        var store = StoreOptions(opts =>
        {
            opts.Schema.For<ConcurrencyDoc>().UseOptimisticConcurrency(true);
        });

        var mapping = (DocumentMapping)store.Options.Storage.FindMapping(typeof(ConcurrencyDoc));
        ClosedShapeRegistration.IsSupported(mapping).ShouldBeTrue();
    }

    [Fact]
    public void IsSupported_accepts_numeric_revisions()
    {
        // M8: numeric revisions is inside the coverage envelope.
        var store = StoreOptions(opts =>
        {
            opts.Schema.For<ConcurrencyDoc>().UseNumericRevisions(true);
        });

        var mapping = (DocumentMapping)store.Options.Storage.FindMapping(typeof(ConcurrencyDoc));
        ClosedShapeRegistration.IsSupported(mapping).ShouldBeTrue();
    }

    [Fact]
    public void IsSupported_accepts_soft_delete()
    {
        // M9: soft delete is inside the coverage envelope.
        var store = StoreOptions(opts =>
        {
            opts.Schema.For<SoftDeleteDoc>().SoftDeleted();
        });

        var mapping = (DocumentMapping)store.Options.Storage.FindMapping(typeof(SoftDeleteDoc));
        ClosedShapeRegistration.IsSupported(mapping).ShouldBeTrue();
    }
}

public class DefaultPathDoc
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class ConcurrencyDoc
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class SoftDeleteDoc
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class DuplicatedFieldDoc
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}
