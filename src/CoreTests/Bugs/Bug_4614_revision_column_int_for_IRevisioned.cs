using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Metadata;
using Marten.Storage.Metadata;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql;
using Xunit;

namespace CoreTests.Bugs;

/// <summary>
/// #4614 — Marten 8 → 9 upgrade was migrating the mt_version column on
/// SingleStreamProjection aggregate document tables from <c>integer</c> to
/// <c>bigint</c>. That widening was a side-effect of <c>RevisionColumn</c>
/// becoming <c>MetadataColumn&lt;long&gt;</c> in #3733; the .NET-side
/// <c>IRevisioned.Version</c> was reverted to int in #4533, but the column
/// width was not. The fix splits the variant in two: IRevisioned-backed
/// documents (the V8 default; what SingleStreamProjection aggregates use)
/// get <c>integer</c>; ILongVersioned-backed documents (MultiStreamProjection's
/// shape, where Version is a global event-sequence number) keep <c>bigint</c>.
///
/// <para>Migration is non-destructive in both directions:</para>
/// <list type="bullet">
///   <item>V8 schema (<c>integer</c>) + IRevisioned (desired <c>integer</c>) — no migration.</item>
///   <item>V8 schema (<c>integer</c>) + ILongVersioned (desired <c>bigint</c>) — widen to bigint.</item>
///   <item>9.x deployment already migrated to <c>bigint</c> + IRevisioned (desired <c>integer</c>)
///     — tolerated, no force-narrow (a USING cast would risk silent data loss).</item>
/// </list>
/// </summary>
public class Bug_4614_revision_column_int_for_IRevisioned: OneOffConfigurationsContext
{
    // ---- Fresh-creation tests: new tables match the V8 column width ----

    [Fact]
    public async Task fresh_table_for_IRevisioned_uses_integer_column()
    {
        StoreOptions(opts => opts.Schema.For<RevisionedDoc>());
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var actualType = await readVersionColumnType(typeof(RevisionedDoc));
        actualType.ShouldBe("integer");
    }

    [Fact]
    public async Task fresh_table_for_ILongVersioned_uses_bigint_column()
    {
        StoreOptions(opts => opts.Schema.For<LongVersionedDoc>());
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var actualType = await readVersionColumnType(typeof(LongVersionedDoc));
        actualType.ShouldBe("bigint");
    }

    // ---- Migration tolerance — the regression's user-visible surface ----

    [Fact]
    public async Task V8_integer_column_for_IRevisioned_is_not_migrated_to_bigint()
    {
        // Stage 1: stand up the schema, then deliberately seed the V8 shape — a real V8
        // deployment's table for an IRevisioned document had `mt_version integer`. On the
        // current code (post-fix), the desired type is integer too, so apply should detect
        // no work to do and leave the column alone — i.e. NO `ALTER COLUMN … TYPE bigint`
        // gets emitted on the V8 → V9 upgrade.
        StoreOptions(opts => opts.Schema.For<RevisionedDoc>());
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        await using (var conn = new NpgsqlConnection(ConnectionSource.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.CreateCommand(
                $"alter table {SchemaName}.mt_doc_revisioneddoc alter column mt_version type integer using mt_version::integer")
                .ExecuteNonQueryAsync();
        }

        // Second apply must be a no-op for this column — the desired-vs-actual diff sees
        // "integer integer", not "bigint integer", and emits nothing.
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        (await readVersionColumnType(typeof(RevisionedDoc))).ShouldBe("integer");
    }

    [Fact]
    public async Task V8_integer_column_for_ILongVersioned_still_widens_to_bigint()
    {
        // The legitimate V8 → V9 widening path is preserved for ILongVersioned-typed
        // documents (the only docs that legitimately need a bigint column going forward).
        StoreOptions(opts => opts.Schema.For<LongVersionedDoc>());
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        await using (var conn = new NpgsqlConnection(ConnectionSource.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.CreateCommand(
                $"alter table {SchemaName}.mt_doc_longversioneddoc alter column mt_version type integer using mt_version::integer")
                .ExecuteNonQueryAsync();
        }

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        (await readVersionColumnType(typeof(LongVersionedDoc))).ShouldBe("bigint");
    }

    [Fact]
    public async Task existing_9x_bigint_column_for_IRevisioned_is_tolerated_not_narrowed()
    {
        // The reverse-direction safety: a deployment that already migrated to V9-with-bigint
        // (before this fix) MUST NOT get force-narrowed to integer on the next apply — a
        // `USING mt_version::integer` cast would silently truncate any out-of-range value.
        // The diff treats bigint-actual + integer-desired as compatible (no SQL emitted).
        StoreOptions(opts => opts.Schema.For<RevisionedDoc>());
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        await using (var conn = new NpgsqlConnection(ConnectionSource.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.CreateCommand(
                $"alter table {SchemaName}.mt_doc_revisioneddoc alter column mt_version type bigint")
                .ExecuteNonQueryAsync();
        }

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        (await readVersionColumnType(typeof(RevisionedDoc))).ShouldBe("bigint");
    }

    [Fact]
    public async Task existing_9x_bigint_column_for_IRevisioned_is_tolerated_not_narrowed_assert_check()
    {
        // The reverse-direction safety: a deployment that already migrated to V9-with-bigint
        // (before this fix) MUST NOT get force-narrowed to integer on the next apply — a
        // `USING mt_version::integer` cast would silently truncate any out-of-range value.
        // The diff treats bigint-actual + integer-desired as compatible (no SQL emitted).
        StoreOptions(opts => opts.Schema.For<RevisionedDoc>());
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        await using (var conn = new NpgsqlConnection(ConnectionSource.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.CreateCommand(
                    $"alter table {SchemaName}.mt_doc_revisioneddoc alter column mt_version type bigint")
                .ExecuteNonQueryAsync();
        }

        await theStore.Storage.Database.AssertDatabaseMatchesConfigurationAsync().ShouldNotThrowAsync();
    }

    // ---- CRUD round-trip on both shapes ----

    [Fact]
    public async Task IRevisioned_round_trip_insert_update_read()
    {
        StoreOptions(opts => opts.Schema.For<RevisionedDoc>());

        var doc = new RevisionedDoc { Id = Guid.NewGuid(), Name = "alpha" };

        await using (var session = theStore.LightweightSession())
        {
            session.UpdateRevision(doc, 1);
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            doc.Name = "beta";
            session.UpdateRevision(doc, 2);
            await session.SaveChangesAsync();
        }

        await using (var query = theStore.QuerySession())
        {
            var loaded = await query.LoadAsync<RevisionedDoc>(doc.Id);
            loaded.ShouldNotBeNull();
            loaded.Name.ShouldBe("beta");
            loaded.Version.ShouldBe(2);
        }
    }

    [Fact]
    public async Task ILongVersioned_round_trip_insert_update_read()
    {
        StoreOptions(opts => opts.Schema.For<LongVersionedDoc>());

        var doc = new LongVersionedDoc { Id = Guid.NewGuid(), Name = "alpha" };

        await using (var session = theStore.LightweightSession())
        {
            session.UpdateRevision(doc, 1);
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            doc.Name = "beta";
            session.UpdateRevision(doc, 2);
            await session.SaveChangesAsync();
        }

        await using (var query = theStore.QuerySession())
        {
            var loaded = await query.LoadAsync<LongVersionedDoc>(doc.Id);
            loaded.ShouldNotBeNull();
            loaded.Name.ShouldBe("beta");
            loaded.Version.ShouldBe(2L);
        }
    }

    // ---- SingleStreamProjection registration auto-picks the right variant ----

    [Fact]
    public async Task SingleStreamProjection_of_IRevisioned_document_gets_integer_column()
    {
        StoreOptions(opts =>
        {
            opts.Events.AddEventType<NameChanged>();
            opts.Projections.Add<NamedAggregateProjection>(ProjectionLifecycle.Inline);
        });

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        (await readVersionColumnType(typeof(NamedAggregate))).ShouldBe("integer");

        // Round-trip through the actual projection path to prove the integer column
        // works end-to-end: append event → inline projection writes the doc → read back.
        var streamId = Guid.NewGuid();
        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream<NamedAggregate>(streamId, new NameChanged("first"));
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            session.Events.Append(streamId, new NameChanged("second"));
            await session.SaveChangesAsync();
        }

        await using (var query = theStore.QuerySession())
        {
            var agg = await query.LoadAsync<NamedAggregate>(streamId);
            agg.ShouldNotBeNull();
            agg.Name.ShouldBe("second");
            agg.Version.ShouldBe(2);
        }
    }

    // ---- helper ----

    private async Task<string> readVersionColumnType(Type docType)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        var tableName = "mt_doc_" + docType.Name.ToLowerInvariant();
        var dataType = (string?)await conn.CreateCommand(
            "select data_type from information_schema.columns where table_schema = :s and table_name = :t and column_name = 'mt_version'")
            .With("s", SchemaName)
            .With("t", tableName)
            .ExecuteScalarAsync();
        return dataType ?? throw new InvalidOperationException(
            $"mt_version column not found on {SchemaName}.{tableName}");
    }
}

public class RevisionedDoc: IRevisioned
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Version { get; set; }
}

public class LongVersionedDoc: ILongVersioned
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public long Version { get; set; }
}

public record NameChanged(string Name);

public class NamedAggregate: IRevisioned
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Version { get; set; }
}

public partial class NamedAggregateProjection: SingleStreamProjection<NamedAggregate, Guid>
{
    public void Apply(NameChanged @event, NamedAggregate agg) => agg.Name = @event.Name;
}
