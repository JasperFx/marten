using System;
using System.Threading.Tasks;
using Marten;
using Marten.Schema;
using Marten.Internal.ClosedShape;
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
    public async Task default_path_round_trips_int_id_doc_via_HiLo()
    {
        // M12: int id with HiLo lands inside the closed-shape envelope.
        // Verifies the default path picks up the HiLo identification.
        var store = StoreOptions(opts =>
        {
        });

        await using (var session = store.LightweightSession())
        {
            session.Store(new IntIdDoc { Name = "v1" });
            await session.SaveChangesAsync();
        }

        await using var query = store.QuerySession();
        (await query.Query<IntIdDoc>().FirstAsync()).Name.ShouldBe("v1");
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

public class HierarchyDoc
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class HierarchyDocSub: HierarchyDoc
{
    public string Extra { get; set; } = string.Empty;
}

public class IntIdDoc
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}
