#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using JasperFx.Events.Tags;
using Marten;
using Marten.Events;
using Marten.Events.Schema;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Core;
using Xunit;

namespace EventSourcingTests.Dcb;

/// <summary>
///     #6035: every <c>mt_dcb_tag_version</c> value comes from a sequence. A store upgrading from Marten 9
///     already has rows, counted from 1, and they have to keep working.
/// </summary>
public class Bug_6035_dcb_tag_version_sequence: OneOffConfigurationsContext
{
    private void configure(AutoCreate autoCreate = AutoCreate.All, bool cleanAll = true)
    {
        StoreOptions(opts =>
        {
            opts.AutoCreateSchemaObjects = autoCreate;
            opts.Events.AddEventType<Bug6035Happened>();
            opts.Events.RegisterTagType<Bug6035TagId>("sequenced_course");
        }, cleanAll);
    }

    private async Task executeAsync(string sql)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<long> scalarAsync(string sql)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private Task<long> sequencesAsync()
        => scalarAsync(
            $"select count(*) from information_schema.sequences where sequence_schema = '{SchemaName}' and sequence_name = '{DcbTagVersionTable.SequenceName}'");

    private async Task appendAsync(Bug6035TagId course)
    {
        await using var session = theStore.LightweightSession();
        var tagged = session.Events.BuildEvent(new Bug6035Happened("one"));
        tagged.WithTag(course);
        session.Events.Append(Guid.NewGuid(), tagged);
        await session.SaveChangesAsync();
    }

    /// <summary>What a Marten 9 store has: rows counted from 1, and no sequence.</summary>
    private async Task becomeAnOlderStoreAsync(Bug6035TagId course, long version)
    {
        configure();
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        await appendAsync(course);
        await executeAsync(
            $"update {SchemaName}.mt_dcb_tag_version set version = {version}; drop sequence {SchemaName}.{DcbTagVersionTable.SequenceName};");
    }

    [Fact]
    public async Task the_sequence_starts_above_a_counted_row()
    {
        configure();
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        (await sequencesAsync()).ShouldBe(1);
        (await scalarAsync(
                $"select start_value::bigint from information_schema.sequences where sequence_schema = '{SchemaName}' and sequence_name = '{DcbTagVersionTable.SequenceName}'"))
            .ShouldBe(DcbTagVersionTable.SequenceStart);
    }

    [Fact]
    public async Task a_store_with_no_tag_types_has_no_sequence()
    {
        StoreOptions(opts => opts.Events.AddEventType<Bug6035Happened>());
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        (await sequencesAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task completely_remove_all_takes_the_sequence()
    {
        configure();
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        await theStore.Advanced.Clean.CompletelyRemoveAllAsync();

        (await sequencesAsync()).ShouldBe(0);
    }

    /// <summary>The table's shape doesn't change, so the sequence is the whole of the migration.</summary>
    [Fact]
    public async Task the_first_migration_of_an_upgraded_store_only_adds_the_sequence()
    {
        var course = new Bug6035TagId(Guid.NewGuid());
        await becomeAnOlderStoreAsync(course, 7);

        configure(cleanAll: false);
        var migration = await theStore.Storage.Database.CreateMigrationAsync();

        migration.Difference.ShouldBe(SchemaPatchDifference.Create);
        var deltas = migration.Deltas.Where(x => x.Difference != SchemaPatchDifference.None).ToArray();
        deltas.Length.ShouldBe(1);
        deltas[0].SchemaObject.Identifier.Name.ShouldBe(DcbTagVersionTable.SequenceName);

        await Should.ThrowAsync<Exception>(() => theStore.Storage.Database.AssertDatabaseMatchesConfigurationAsync());

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        (await theStore.Storage.Database.CreateMigrationAsync()).Difference.ShouldBe(SchemaPatchDifference.None);
        await theStore.Storage.Database.AssertDatabaseMatchesConfigurationAsync();

        // The row kept its value, because a boundary in flight across the upgrade may have captured it
        (await scalarAsync($"select version from {SchemaName}.mt_dcb_tag_version where position(':' in tag_table) = 0"))
            .ShouldBe(7);
    }

    [Theory]
    [InlineData(AutoCreate.All)]
    [InlineData(AutoCreate.CreateOrUpdate)]
    [InlineData(AutoCreate.CreateOnly)]
    public async Task a_session_on_an_upgraded_store_creates_the_sequence_for_itself(AutoCreate autoCreate)
    {
        var course = new Bug6035TagId(Guid.NewGuid());
        await becomeAnOlderStoreAsync(course, 7);

        configure(autoCreate, cleanAll: false);
        await appendAsync(course);

        (await scalarAsync($"select version from {SchemaName}.mt_dcb_tag_version where position(':' in tag_table) = 0"))
            .ShouldBeGreaterThanOrEqualTo(DcbTagVersionTable.SequenceStart);
    }

    /// <summary>A row that counted up from 1 is below everything the sequence hands out.</summary>
    [Fact]
    public async Task an_upgraded_row_moves_above_its_old_value_and_a_capture_of_it_still_works()
    {
        var course = new Bug6035TagId(Guid.NewGuid());
        var query = new EventTagQuery().Or(course);
        await becomeAnOlderStoreAsync(course, 7);
        configure(cleanAll: false);
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        // Read the old value, then let a second boundary move the row past it
        await using var stale = theStore.LightweightSession();
        var staleBoundary = await stale.Events.FetchForWritingByTags<Bug6035Boundary>(query);

        await using (var session = theStore.LightweightSession())
        {
            var boundary = await session.Events.FetchForWritingByTags<Bug6035Boundary>(query);
            var tagged = session.Events.BuildEvent(new Bug6035Happened("two"));
            tagged.WithTag(course);
            boundary.AppendOne(tagged);
            await session.SaveChangesAsync();
        }

        (await scalarAsync($"select version from {SchemaName}.mt_dcb_tag_version where position(':' in tag_table) = 0"))
            .ShouldBeGreaterThanOrEqualTo(DcbTagVersionTable.SequenceStart);

        var late = stale.Events.BuildEvent(new Bug6035Happened("three"));
        late.WithTag(course);
        staleBoundary.AppendOne(late);
        await Should.ThrowAsync<DcbConcurrencyException>(() => stale.SaveChangesAsync());
    }

    /// <summary>
    ///     What <c>AutoCreate.None</c> is for, and the one way to be caught out by this: nothing creates the
    ///     sequence, and an append under a tag can't run without it.
    /// </summary>
    [Fact]
    public async Task auto_create_none_reports_the_missing_sequence()
    {
        var course = new Bug6035TagId(Guid.NewGuid());
        await becomeAnOlderStoreAsync(course, 7);

        configure(AutoCreate.None, cleanAll: false);

        var ex = await Should.ThrowAsync<Exception>(() => appendAsync(course));
        ex.ToString().ShouldContain(DcbTagVersionTable.SequenceName);
    }
}
