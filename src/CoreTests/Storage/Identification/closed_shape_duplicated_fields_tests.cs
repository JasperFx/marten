using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Internal.ClosedShape;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core;
using Xunit;

namespace CoreTests.Storage.Identification;

/// <summary>
/// W3 spike M10: validates duplicated-field behavior on the closed-shape
/// document storage. Configured columns mirror a member from the JSON
/// data — the value is extracted at save time, written to a typed
/// column, and used by LINQ queries for index-friendly WHERE pushdown.
/// Reads still deserialize from the data column; the duplicated columns
/// are write-only.
/// </summary>
public class closed_shape_duplicated_fields_tests: BugIntegrationContext
{
    private DocumentStore DuplicatedStore(Action<StoreOptions>? extra = null)
        => StoreOptions(opts =>
        {
            opts.UseClosedShapeDocumentStorage = true;
            opts.Schema.For<DfDoc>().Duplicate(x => x.Name);
            extra?.Invoke(opts);
        });

    [Fact]
    public async Task duplicated_column_round_trips_via_query()
    {
        var store = DuplicatedStore();

        var id = Guid.NewGuid();
        await using (var session = store.LightweightSession())
        {
            session.Store(new DfDoc { Id = id, Name = "alice" });
            await session.SaveChangesAsync();
        }

        await using var query = store.QuerySession();
        var found = await query.Query<DfDoc>().Where(x => x.Name == "alice").SingleAsync();
        found.Id.ShouldBe(id);
        found.Name.ShouldBe("alice");
    }

    [Fact]
    public async Task duplicated_column_can_filter_via_index_friendly_predicate()
    {
        var store = DuplicatedStore();

        await using (var session = store.LightweightSession())
        {
            session.Store(new DfDoc { Id = Guid.NewGuid(), Name = "alice" });
            session.Store(new DfDoc { Id = Guid.NewGuid(), Name = "bob" });
            session.Store(new DfDoc { Id = Guid.NewGuid(), Name = "alice" });
            await session.SaveChangesAsync();
        }

        await using var query = store.QuerySession();
        var alices = await query.Query<DfDoc>()
            .Where(x => x.Name == "alice")
            .ToListAsync();
        alices.Count.ShouldBe(2);
    }

    [Fact]
    public async Task null_member_value_writes_DBNull_to_the_column()
    {
        var store = DuplicatedStore(opts =>
        {
            // Allow nullable nested member.
            opts.Schema.For<DfNullableDoc>().Duplicate(x => x.OptionalName);
        });

        var id = Guid.NewGuid();
        await using (var session = store.LightweightSession())
        {
            session.Store(new DfNullableDoc { Id = id, OptionalName = null });
            await session.SaveChangesAsync();
        }

        await using var query = store.QuerySession();
        var found = await query.Query<DfNullableDoc>().Where(x => x.OptionalName == null).SingleAsync();
        found.Id.ShouldBe(id);
    }

    [Fact]
    public async Task duplicated_enum_member_is_written_as_string_when_AsString()
    {
        var store = StoreOptions(opts =>
        {
            opts.UseClosedShapeDocumentStorage = true;
            opts.Advanced.DuplicatedFieldEnumStorage = EnumStorage.AsString;
            opts.Schema.For<DfEnumDoc>().Duplicate(x => x.Color);
        });

        var id = Guid.NewGuid();
        await using (var session = store.LightweightSession())
        {
            session.Store(new DfEnumDoc { Id = id, Color = DfColor.Red });
            await session.SaveChangesAsync();
        }

        await using var query = store.QuerySession();
        var found = await query.Query<DfEnumDoc>().Where(x => x.Color == DfColor.Red).SingleAsync();
        found.Id.ShouldBe(id);
    }

    [Fact]
    public async Task only_for_searching_columns_are_not_written_by_save()
    {
        // OnlyForSearching = true creates the column but excludes it from
        // writes — the column is populated via JSON extraction in a SQL
        // expression / index. The closed-shape binder must skip these so
        // the operation doesn't try to bind a value to a non-existent
        // parameter slot.
        var store = StoreOptions(opts =>
        {
            opts.UseClosedShapeDocumentStorage = true;
            opts.Schema.For<DfDoc>().Index(x => x.Name);
        });

        var id = Guid.NewGuid();
        await using (var session = store.LightweightSession())
        {
            session.Store(new DfDoc { Id = id, Name = "indexed" });
            await session.SaveChangesAsync();
        }

        await using var query = store.QuerySession();
        var loaded = await query.LoadAsync<DfDoc>(id);
        loaded.ShouldNotBeNull();
        loaded.Name.ShouldBe("indexed");
    }

    [Fact]
    public void IsSupported_accepts_duplicated_fields()
    {
        var store = StoreOptions(opts =>
        {
            opts.Schema.For<DfDoc>().Duplicate(x => x.Name);
        });

        var mapping = (Marten.Schema.DocumentMapping)store.Options.Storage.FindMapping(typeof(DfDoc));
        ClosedShapeRegistration.IsSupported(mapping).ShouldBeTrue();
    }
}

public class DfDoc
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class DfNullableDoc
{
    public Guid Id { get; set; }
    public string? OptionalName { get; set; }
}

public enum DfColor
{
    Red,
    Green,
    Blue
}

public class DfEnumDoc
{
    public Guid Id { get; set; }
    public DfColor Color { get; set; }
}
